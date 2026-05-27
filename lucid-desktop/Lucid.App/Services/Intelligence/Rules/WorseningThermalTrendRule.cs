using Lucid.Helpers;
using Lucid.Services.Telemetry;

namespace Lucid.Services.Intelligence.Rules;

/// <summary>
/// Fires when CPU temperature has been climbing steadily over a sustained period.
///
/// Unlike <see cref="HighCpuTemperatureRule"/> — which fires when temperature
/// is already above a static threshold — this rule fires while temperature is
/// on the way up. This gives users a warning before thermal throttling begins,
/// while there is still time to improve airflow or reduce system load.
///
/// Prerequisites:
///   Requires <c>CpuTemperatureAvailable = true</c> on the current snapshot.
///   Temperature must be above a comfortable baseline (65 °C) to avoid
///   flagging normal warm-up during the first few minutes after boot.
///
/// Detection method:
///   Window delta: the 5-minute average temperature must be at least 3 °C
///   higher than the 30-minute average, confirming a sustained rise rather
///   than a brief spike. The regression slope must also be positive.
///
///   Note: <see cref="TelemetryMetric.Temperature"/> stores raw °C values in the
///   history buffer. Stats averages are therefore in degrees Celsius.
///
/// Minimum data requirements:
///   ≥ 80 samples in the 30-minute window (≈ 2 minutes of data).
///
/// Confidence:
///   Scales from 65 % at minimum data to 88 % at full 30-minute coverage.
/// </summary>
public sealed class WorseningThermalTrendRule : IInsightRule
{
    // Minimum temperature to be above before flagging a trend (avoids boot warm-up)
    private const double BaselineFloor       = 65.0;
    // 5-min average must be this many °C above the 30-min average
    private const double MinTempDeltaCelsius = 3.0;
    // Regression slope must be upward (°C/min)
    private const double MinSlopePerMinute   = 0.10;
    // Minimum samples in the 30-min window
    private const int    MinLongSamples      = 80;

    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.thermal.improve-airflow",
            Title:                "Improve Airflow",
            Description:          "Ensure PC vents are unobstructed and the case is in open air. Repositioning the PC can reduce temperatures by several degrees immediately.",
            Impact:               ActionImpact.Moderate,
            Effort:               ActionEffort.Seconds,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),

        new SystemAction(
            Id:                   "action.thermal.reduce-cpu-load",
            Title:                "Reduce CPU Load",
            Description:          "Closing CPU-intensive applications gives the processor a chance to idle and cool down. Even brief idle periods can reverse a rising temperature trend.",
            Impact:               ActionImpact.High,
            Effort:               ActionEffort.Seconds,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),

        new SystemAction(
            Id:                   "action.thermal.check-cooler",
            Title:                "Check CPU Cooler",
            Description:          "A steadily rising temperature may indicate the heatsink is clogged with dust or the thermal paste has dried out. Cleaning or reapplying can restore 10–20 °C.",
            Impact:               ActionImpact.Significant,
            Effort:               ActionEffort.TenMinutes,
            Risk:                 ActionRisk.Moderate,
            RequiresConfirmation: true,
            IsReversible:         true,
            ConfirmationMessage:  "Opening your PC requires shutting down first. Incorrectly reseating the cooler could damage the CPU."),
    ];

    public string RuleId => "cpu.thermal-trend";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        // Gate 1: only proceed when ACPI thermal zones are exposed on this hardware
        if (!current.CpuTemperatureAvailable) return null;

        // Gate 2: temperature must be above a warm baseline to avoid flagging
        //         normal post-boot warm-up from 40 °C → 55 °C.
        if (current.CpuTemperatureCelsius < BaselineFloor) return null;

        // Gate 3: require sufficient thermal history
        var stats5m  = history.GetStats(TelemetryMetric.Temperature, TimeWindow.LastFiveMinutes);
        var stats30m = history.GetStats(TelemetryMetric.Temperature, TimeWindow.LastThirtyMinutes);

        if (stats5m.IsEmpty || stats30m.IsEmpty || stats30m.SampleCount < MinLongSamples)
            return null;

        // Gate 4: the recent average must be meaningfully higher than the long average
        double delta = stats5m.Average - stats30m.Average;
        if (delta < MinTempDeltaCelsius) return null;

        // Gate 5: confirm via regression that the temperature is genuinely rising
        var    samples = history.GetSamples(TelemetryMetric.Temperature, TimeWindow.LastThirtyMinutes);
        double slope   = TrendAnalysis.SlopePerMinute(samples);
        if (slope < MinSlopePerMinute) return null;

        int    confidence  = TrendAnalysis.ConfidenceFromCoverage(stats30m.SampleCount,
                                 TimeWindow.LastThirtyMinutes, min: 65, max: 88);
        string elapsed     = TrendAnalysis.FormatElapsed(stats30m.SampleCount);
        double currentTemp = current.CpuTemperatureCelsius;

        // Estimate time to reach the throttling threshold at current slope (°C/min)
        double toThrottle    = 90.0 - currentTemp;
        bool   closeToThrottle = toThrottle > 0 && slope > 0 && (toThrottle / slope) < 20;
        string riskNote = closeToThrottle
            ? $" At the current rate the processor could reach thermal throttling within {(int)(toThrottle / slope)} minutes."
            : "";

        return new SystemInsight(
            Id:       RuleId,
            Severity: InsightSeverity.Warning,
            Title:    "CPU Temperature Is Rising",
            Detail:   $"CPU temperature has climbed from an average of {stats30m.Average:0}°C " +
                      $"to {stats5m.Average:0}°C over the last {elapsed} " +
                      $"(currently {currentTemp:0}°C). " +
                      $"A sustained upward trend can indicate poor airflow, " +
                      $"accumulated dust, or a workload that is holding the processor " +
                      $"at high clock speeds for an extended period.{riskNote}",
            ActionHint:        "Check airflow and consider closing CPU-intensive applications to give the processor time to cool.",
            DetectedAt:        DateTimeOffset.Now,
            ConfidencePercent: confidence,
            RecommendedActions: s_actions);
    }
}
