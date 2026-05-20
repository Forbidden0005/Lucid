using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Baseline;
using ExplainMyPC.Services.Telemetry;

namespace ExplainMyPC.Services.Intelligence.Rules;

/// <summary>
/// Fires when the CPU temperature is anomalously high relative to THIS
/// machine's learned thermal operating range.
///
/// Every machine has a unique thermal signature: a thin laptop runs hotter
/// than a tower PC; a machine in a hot room has a higher baseline than one
/// in a climate-controlled office. This rule learns the machine's normal
/// thermal range and flags deviations from IT, not from some universal limit.
///
/// The absolute 80 °C+ threshold rule (<see cref="HighCpuTemperatureRule"/>)
/// remains the hard safety boundary. This rule fires earlier — when the
/// temperature is significantly above what this specific machine normally runs —
/// to provide an early warning before the absolute limit is approached.
///
/// Thresholds:
///   Minimum absolute delta  ≥ 5 °C above normal thermal mean
///   Z-score ≥ 2.0 → Recommendation
///   Z-score ≥ 3.0 → Warning
///
/// Guards:
///   • Only fires when the ACPI thermal sensor is available.
///   • Only fires when temperature > 20 °C (plausible range).
///   • Returns null until the baseline has ≥ 200 thermal observations.
///
/// ID: baseline.thermal-above-norm
/// </summary>
public sealed class BaselineThermalAnomalyRule : IInsightRule
{
    private const double MinValidTemperature = 20.0;

    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.thermal.check-ventilation",
            Title:                "Check PC Ventilation",
            Description:          "Ensure your PC's vents are not blocked and the fans are running. Dust buildup is the most common cause of unexpectedly high temperatures.",
            Impact:               ActionImpact.High,
            Effort:               ActionEffort.FewMinutes,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),
    ];

    private readonly ISystemBaselineService _baseline;

    public string RuleId => "baseline.thermal-above-norm";

    public BaselineThermalAnomalyRule(ISystemBaselineService baseline)
    {
        _baseline = baseline;
    }

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        // Require a working thermal sensor.
        if (!current.CpuTemperatureAvailable)
            return null;

        double tempC = current.CpuTemperatureCelsius;
        if (tempC < MinValidTemperature)
            return null;

        // Guard: wait for the baseline warm-up period.
        var baseline = _baseline.CurrentBaseline;
        if (baseline is null || !baseline.NormalThermalReady)
            return null;

        double zscore = baseline.ThermalZScore(tempC);

        if (!baseline.IsThermalAnomaly(tempC))
            return null;

        bool    isWarning  = zscore >= MachineBaseline.ThermalSigmaWarning;
        var     severity   = isWarning ? InsightSeverity.Warning : InsightSeverity.Recommendation;
        int     confidence = MachineBaseline.ConfidenceFromZScore(zscore);

        double normalLow  = Math.Max(0, baseline.NormalThermalMean - baseline.NormalThermalStdDev);
        double normalHigh = baseline.NormalThermalMean + baseline.NormalThermalStdDev;

        string detail = isWarning
            ? $"Your CPU temperature is {tempC:0}°C — {zscore:F1} standard deviations above your machine's " +
              $"normal operating range of {normalLow:0}–{normalHigh:0}°C. " +
              $"While still within hardware safety limits, this is significantly hotter than this PC " +
              $"typically runs, which may indicate a cooling issue or unusually heavy workload."
            : $"Your CPU temperature ({tempC:0}°C) is above your machine's normal range of " +
              $"{normalLow:0}–{normalHigh:0}°C. This machine usually runs cooler than it does right now. " +
              $"Check whether a demanding application is running, or whether the vents need cleaning.";

        return new SystemInsight(
            Id:       RuleId,
            Severity: severity,
            Title:    isWarning
                          ? "Temperature Significantly Above Your Normal"
                          : "Temperature Above Your Machine's Typical Range",
            Detail:            detail,
            ActionHint:        "Check that your PC's fans are running and vents are not blocked.",
            DetectedAt:        DateTimeOffset.Now,
            ConfidencePercent: confidence,
            RecommendedActions: s_actions);
    }
}
