using Lucid.Helpers;
using Lucid.Services.Telemetry;

namespace Lucid.Services.Intelligence.Rules;

/// <summary>
/// Fires when the CPU temperature exceeds safe operating thresholds.
///
/// Silently skips on systems where ACPI thermal zones are not exposed
/// (VMs, certain OEM hardware configurations) — CpuTemperatureAvailable
/// is the gate.
///
/// Thresholds:
///   Warning  — above 80 °C: approaching thermal limits for most desktop
///              and laptop CPUs; sustained operation here accelerates wear.
///   Critical — at or above 90 °C: thermal throttling is actively
///              reducing clock speed to protect the chip, causing
///              measurable performance loss.
/// </summary>
public sealed class HighCpuTemperatureRule : IInsightRule
{
    private const double WarningThreshold  = 80.0;
    private const double CriticalThreshold = 90.0;

    // Allocated once at class load — no per-tick heap pressure.
    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.thermal.improve-airflow",
            Title:                "Improve Airflow",
            Description:          "Ensure the PC's air vents are unobstructed and facing open space. Moving it off carpet or away from walls can lower temperatures by several degrees.",
            Impact:               ActionImpact.Moderate,
            Effort:               ActionEffort.Seconds,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),

        new SystemAction(
            Id:                   "action.thermal.check-cooler",
            Title:                "Check CPU Cooler",
            Description:          "Verify the CPU fan is spinning and the heatsink is properly seated. Reapplying thermal paste can reduce temperatures by 10–20 °C on older systems.",
            Impact:               ActionImpact.Significant,
            Effort:               ActionEffort.TenMinutes,
            Risk:                 ActionRisk.Moderate,
            RequiresConfirmation: true,
            IsReversible:         true,
            ConfirmationMessage:  "Opening your PC and touching the cooler requires shutting down first. Incorrectly reseating the cooler could damage the CPU."),
    ];

    public string RuleId => "cpu.high-temperature";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        if (!current.CpuTemperatureAvailable)
            return null;

        if (current.CpuTemperatureCelsius <= WarningThreshold)
            return null;

        bool    isCritical = current.CpuTemperatureCelsius >= CriticalThreshold;
        string? hint       = isCritical
            ? "Ensure PC vents are clear and the CPU fan is spinning. Consider reapplying thermal paste."
            : null;

        return new SystemInsight(
            Id:                 RuleId,
            Severity:           InsightSeverity.Warning,
            Title:              isCritical ? "CPU Temperature Critical" : "CPU Running Hot",
            Detail:             $"Your CPU is currently at {current.CpuTemperatureCelsius:0}°C. " +
                                (isCritical
                                    ? "At this temperature the processor is likely throttling its speed to prevent " +
                                      "damage, which can cause noticeable performance drops."
                                    : "This is approaching the upper end of a comfortable operating range. " +
                                      "Sustained high temperatures can reduce component lifespan."),
            ActionHint:         hint,
            DetectedAt:         DateTimeOffset.Now,
            RecommendedActions: s_actions);
    }
}
