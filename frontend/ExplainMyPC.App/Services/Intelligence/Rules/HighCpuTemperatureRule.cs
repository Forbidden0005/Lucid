using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Telemetry;

namespace ExplainMyPC.Services.Intelligence.Rules;

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
            Id:         RuleId,
            Severity:   InsightSeverity.Warning,
            Title:      isCritical ? "CPU Temperature Critical" : "CPU Running Hot",
            Detail:     $"Your CPU is currently at {current.CpuTemperatureCelsius:0}°C. " +
                        (isCritical
                            ? "At this temperature the processor is likely throttling its speed to prevent " +
                              "damage, which can cause noticeable performance drops."
                            : "This is approaching the upper end of a comfortable operating range. " +
                              "Sustained high temperatures can reduce component lifespan."),
            ActionHint: hint,
            DetectedAt: DateTimeOffset.Now);
    }
}
