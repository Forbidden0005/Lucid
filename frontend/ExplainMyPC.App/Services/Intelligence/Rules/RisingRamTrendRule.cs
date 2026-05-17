using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Telemetry;

namespace ExplainMyPC.Services.Intelligence.Rules;

/// <summary>
/// Fires when RAM usage has been climbing steadily over the last several minutes.
///
/// Unlike <see cref="ElevatedRamPressureRule"/> — which fires when RAM is already
/// high — this rule fires <em>while RAM is on the way up</em>, giving users an
/// earlier warning before swapping begins.
///
/// Detection method:
///   Multi-window delta: the 5-minute average must be meaningfully higher than
///   the 30-minute average, confirming a sustained upward shift rather than a
///   momentary spike. A least-squares regression over the full 30-minute sample
///   series confirms the slope is genuinely positive.
///
/// Minimum data requirements:
///   ≥ 80 samples in the 30-minute window (≈ 2 minutes of polling) before the
///   rule can fire — prevents false positives during startup.
///
/// Confidence:
///   Scales from 65 % at 2 minutes of data to 90 % at a full 30-minute window.
/// </summary>
public sealed class RisingRamTrendRule : IInsightRule
{
    // RAM must be above this floor (prevents flagging tiny fluctuations at idle)
    private const double BaselineFloor       = 55.0;
    // 5-min avg must exceed 30-min avg by at least this many points
    private const double MinWindowDelta      = 5.0;
    // Overall regression slope must be positive by at least this amount
    private const double MinSlopePerMinute   = 0.20;
    // Minimum samples in the 30-min window before the rule can fire
    private const int    MinLongSamples      = 80;

    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.ram.close-browser-tabs",
            Title:                "Close Browser Tabs",
            Description:          "Each open browser tab holds memory. Closing unused tabs is the fastest way to slow or reverse a rising memory trend.",
            Impact:               ActionImpact.Moderate,
            Effort:               ActionEffort.Seconds,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),

        new SystemAction(
            Id:                   "action.ram.restart-heavy-apps",
            Title:                "Restart Memory-Heavy Apps",
            Description:          "Applications that grow in memory over time (browsers, IDEs, communication apps) release accumulated memory when restarted.",
            Impact:               ActionImpact.High,
            Effort:               ActionEffort.FewMinutes,
            Risk:                 ActionRisk.Low,
            RequiresConfirmation: true,
            IsReversible:         true,
            ConfirmationMessage:  "Restarting an app will close its current windows. Save your work before proceeding."),
    ];

    public string RuleId => "ram.rising-trend";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        // Gate 1: RAM must be above the noise floor
        if (current.RamPercent < BaselineFloor) return null;

        // Gate 2: require enough history to detect a meaningful trend
        var stats5m  = history.GetStats(TelemetryMetric.Ram, TimeWindow.LastFiveMinutes);
        var stats30m = history.GetStats(TelemetryMetric.Ram, TimeWindow.LastThirtyMinutes);

        if (stats5m.IsEmpty || stats30m.IsEmpty || stats30m.SampleCount < MinLongSamples)
            return null;

        // Gate 3: the short-window average must be significantly higher than the long window
        double delta = stats5m.Average - stats30m.Average;
        if (delta < MinWindowDelta) return null;

        // Gate 4: confirm the overall regression slope is genuinely upward
        var    samples = history.GetSamples(TelemetryMetric.Ram, TimeWindow.LastThirtyMinutes);
        double slope   = TrendAnalysis.SlopePerMinute(samples);
        if (slope < MinSlopePerMinute) return null;

        // Build the finding
        int    confidence = TrendAnalysis.ConfidenceFromCoverage(stats30m.SampleCount,
                                TimeWindow.LastThirtyMinutes, min: 65, max: 90);
        string elapsed    = TrendAnalysis.FormatElapsed(stats30m.SampleCount);
        double freeGb     = current.RamTotalGb - current.RamUsedGb;

        var attributions = ProcessAttributionHelper.ForRam(current.TopProcesses, current.RamTotalGb);

        return new SystemInsight(
            Id:       RuleId,
            Severity: InsightSeverity.Warning,
            Title:    "Memory Usage Is Steadily Rising",
            Detail:   $"RAM has climbed from an average of {stats30m.Average:0}% to {stats5m.Average:0}% " +
                      $"over the last {elapsed} (currently {current.RamPercent:0}%, " +
                      $"{freeGb:F1} GB free). At this rate, available memory may become " +
                      $"critically low before the source is addressed.",
            ActionHint:          "Close or restart memory-heavy applications before the trend reaches a critical level.",
            DetectedAt:          DateTimeOffset.Now,
            ConfidencePercent:   confidence,
            RecommendedActions:  s_actions,
            AttributedProcesses: attributions);
    }
}
