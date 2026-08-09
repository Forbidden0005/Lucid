using Lucid.Helpers;
using Lucid.Services.Intelligence.Forecasting;
using Lucid.Services.Telemetry;

namespace Lucid.Services.Intelligence.Rules;

/// <summary>
/// Predicts when RAM usage will reach the memory-pressure threshold (85 %),
/// based on the current rising trend.
///
/// Complementary rules — how they differ:
///   <see cref="ElevatedRamPressureRule"/>  — reactive: fires when RAM is already ≥ 85 %.
///   <see cref="RisingRamTrendRule"/>       — observational: fires when RAM has been climbing.
///   This rule                              — predictive: fires when the trend projects
///                                           reaching 85 % within the next 30 minutes.
///
/// Detection method:
///   Least-squares slope over the 30-minute RAM% history. The slope must be
///   sustained (slope ≥ 0.25 %/min, i.e. ~15 %/hour) and backed by enough
///   history to be reliable. The time-to-threshold calculation uses
///   <see cref="ForecastHelper.MinutesToThreshold"/>.
///
/// Horizon gates:
///   Fires when pressure is projected to arrive within 30 minutes.
///   Severity escalates to Warning when arrival is within 10 minutes.
///
/// Warm-up:
///   Requires ≥ 60 samples (~90 s of history) before the slope is trusted.
///
/// Avoids overlap:
///   • Does not fire if RAM is already ≥ 83 % (ElevatedRamPressureRule handles that).
///   • Does not fire if slope < 0.25 %/min (too slow to be actionable).
///
/// ID: forecast.ram-pressure
/// </summary>
public sealed class RamPressureForecastRule : IInsightRule
{
    private const double PressureThreshold  = 85.0;  // %  — where swapping begins
    private const double MaxDiskRamPct      = 83.0;  // %  — leave room above for other rules
    private const double MinSlope           = 0.25;  // %/min — meaningful upward trend
    private const double WarningMinutes     = 10.0;  // within 10 min → Warning
    private const double MaxHorizonMinutes  = 30.0;  // beyond 30 min → not actionable
    private const int    MinSamples         = 60;    // ≈ 90 s at 1.5 s/tick

    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.ram.close-browser-tabs",
            Title:                "Close Browser Tabs",
            Description:          "Close unused browser tabs to slow or reverse the memory trend before available RAM reaches a critical level.",
            Impact:               ActionImpact.Moderate,
            Effort:               ActionEffort.Seconds,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),

        new SystemAction(
            Id:                   "action.ram.restart-heavy-apps",
            Title:                "Restart Memory-Heavy Apps",
            Description:          "Applications with memory leaks or large working sets will return memory to the system when restarted. Act now to prevent your machine from reaching a swapping state.",
            Impact:               ActionImpact.High,
            Effort:               ActionEffort.FewMinutes,
            Risk:                 ActionRisk.Low,
            RequiresConfirmation: true,
            IsReversible:         true,
            ConfirmationMessage:  "Restarting an app will close its current windows. Save your work before proceeding."),
    ];

    public string RuleId => "forecast.ram-pressure";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        // Already in or past pressure zone — let ElevatedRamPressureRule handle it.
        if (current.RamPercent >= MaxDiskRamPct || current.RamTotalGb <= 0)
            return null;

        var stats30m = history.GetStats(TelemetryMetric.Ram, TimeWindow.LastThirtyMinutes);
        if (stats30m.IsEmpty || stats30m.SampleCount < MinSamples)
            return null;

        // Compute the least-squares slope over the full 30-minute window.
        var    samples = history.GetSamples(TelemetryMetric.Ram, TimeWindow.LastThirtyMinutes);
        double slope   = TrendAnalysis.SlopePerMinute(samples);

        if (slope < MinSlope) return null;

        double minutesToPressure = ForecastHelper.MinutesToThreshold(
            current.RamPercent, slope, PressureThreshold);

        if (minutesToPressure > MaxHorizonMinutes || minutesToPressure <= 0)
            return null;

        bool isWarning = minutesToPressure <= WarningMinutes;
        int  confidence = ForecastHelper.ForecastConfidence(
                              stats30m.SampleCount,
                              TimeWindow.LastThirtyMinutes,
                              minutesToPressure);

        double freeGb      = current.RamTotalGb - current.RamUsedGb;
        string timeDesc    = ForecastHelper.FormatMinuteHorizon(minutesToPressure);
        string rateDesc    = $"{slope * 60:F0} % per hour ({slope:F2} %/min)";

        var attributions = ProcessAttributionHelper.ForRam(current.TopProcesses, current.RamTotalGb);

        return new SystemInsight(
            Id:       RuleId,
            Severity: isWarning ? InsightSeverity.Warning : InsightSeverity.Recommendation,
            Title:    isWarning
                          ? "Memory Pressure Expected Within Minutes"
                          : "RAM on Track to Hit Pressure Zone",
            Detail:   $"RAM is currently at {current.RamPercent:0}% ({current.RamUsedGb:F1} GB used, " +
                      $"{freeGb:F1} GB free) and rising at {rateDesc}. " +
                      $"At this rate your system will reach the memory-pressure threshold " +
                      $"({PressureThreshold:0}%) in {timeDesc}, at which point Windows will " +
                      $"begin actively swapping to disk — causing noticeable slowdowns.",
            ActionHint:          $"Free RAM now to avoid performance degradation in {timeDesc}.",
            DetectedAt:          DateTimeOffset.Now,
            ConfidencePercent:   confidence,
            RecommendedActions:  s_actions,
            AttributedProcesses: attributions);
    }
}
