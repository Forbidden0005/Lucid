using Lucid.Helpers;
using Lucid.Services.Intelligence.Forecasting;
using Lucid.Services.Telemetry;

namespace Lucid.Services.Intelligence.Rules;

/// <summary>
/// Predicts when the CPU temperature will reach the thermal-throttle threshold (85 °C),
/// based on the current rising trend.
///
/// Complementary rules — how they differ:
///   <see cref="HighCpuTemperatureRule"/>    — reactive: fires when CPU is already ≥ 80 °C.
///   <see cref="WorseningThermalTrendRule"/> — observational: fires when temperature is climbing.
///   This rule                               — predictive: fires when the trend projects
///                                            reaching 85 °C within the next 25 minutes.
///
/// Detection method:
///   Least-squares slope over the 30-minute temperature history. The slope must
///   be sustained (slope ≥ 0.15 °C/min) and backed by enough history to be reliable.
///   The time-to-threshold calculation uses <see cref="ForecastHelper.MinutesToThreshold"/>.
///
/// Horizon gate:
///   Fires when throttle temperature is projected to arrive within 25 minutes.
///   Severity is always Warning — thermal throttling is an immediate performance hazard.
///
/// Warm-up:
///   Requires ≥ 40 samples (~60 s of history) before the slope is trusted.
///
/// Avoids overlap:
///   • Does not fire if CPU is already ≥ 78 °C (HighCpuTemperatureRule handles that range).
///   • Does not fire if slope &lt; 0.15 °C/min (too slow to be actionable within horizon).
///   • Does not fire when temperature data is unavailable (VM / unsupported hardware).
///
/// ID: forecast.thermal-runaway
/// </summary>
public sealed class ThermalRunawayForecastRule : IInsightRule
{
    private const double ThrottleThreshold  = 85.0;   // °C — hardware begins throttling
    private const double MaxEntryTemp       = 78.0;   // °C — leave head-room for reactive rule
    private const double MinEntryTemp       = 40.0;   // °C — must be meaningfully warm to forecast
    private const double MinSlope           = 0.15;   // °C/min — meaningful upward thermal trend
    private const double MaxHorizonMinutes  = 25.0;   // beyond 25 min → not actionable
    private const int    MinSamples         = 40;     // ≈ 60 s at 1.5 s/tick

    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.thermal.improve-airflow",
            Title:                "Improve Airflow",
            Description:          "Ensure the PC's air vents are unobstructed. Moving the PC off carpet or away from walls can lower temperatures by several degrees before thermal throttling begins.",
            Impact:               ActionImpact.Moderate,
            Effort:               ActionEffort.Seconds,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),

        new SystemAction(
            Id:                   "action.thermal.reduce-workload",
            Title:                "Reduce Active Workload",
            Description:          "Closing resource-intensive applications will reduce heat generation and slow or stop the temperature rise before the CPU reaches its throttle limit.",
            Impact:               ActionImpact.High,
            Effort:               ActionEffort.Seconds,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),
    ];

    public string RuleId => "forecast.thermal-runaway";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        // Requires functioning thermal sensor.
        if (!current.CpuTemperatureAvailable)
            return null;

        double temp = current.CpuTemperatureCelsius;

        // Must be in a meaningful range — too cold to be approaching throttle,
        // or already so hot the reactive rule will handle it.
        if (temp < MinEntryTemp || temp >= MaxEntryTemp)
            return null;

        var stats30m = history.GetStats(TelemetryMetric.Temperature, TimeWindow.LastThirtyMinutes);
        if (stats30m.IsEmpty || stats30m.SampleCount < MinSamples)
            return null;

        // Compute least-squares slope over the available window.
        var    samples = history.GetSamples(TelemetryMetric.Temperature, TimeWindow.LastThirtyMinutes);
        double slope   = TrendAnalysis.SlopePerMinute(samples);

        if (slope < MinSlope) return null;

        double minutesToThrottle = ForecastHelper.MinutesToThreshold(
            temp, slope, ThrottleThreshold);

        if (minutesToThrottle <= 0 || minutesToThrottle > MaxHorizonMinutes)
            return null;

        int    confidence = ForecastHelper.ForecastConfidence(
                                stats30m.SampleCount,
                                TimeWindow.LastThirtyMinutes,
                                minutesToThrottle);

        string timeDesc  = ForecastHelper.FormatMinuteHorizon(minutesToThrottle);
        string rateDesc  = $"{slope:F2} °C/min";
        double remaining = ThrottleThreshold - temp;

        return new SystemInsight(
            Id:       RuleId,
            Severity: InsightSeverity.Warning,
            Title:    "CPU Heading Toward Thermal Throttle",
            Detail:   $"CPU temperature is currently {temp:0}°C and rising at {rateDesc}. " +
                      $"At this rate it will reach {ThrottleThreshold:0}°C — the thermal " +
                      $"throttle threshold — in {timeDesc} ({remaining:F0}°C to go). " +
                      $"When throttling engages, Windows reduces clock speed to protect " +
                      $"the processor, causing noticeable performance degradation.",
            ActionHint:        $"Act now to slow the temperature rise before throttling begins in {timeDesc}.",
            DetectedAt:        DateTimeOffset.Now,
            ConfidencePercent: confidence,
            RecommendedActions: s_actions);
    }
}
