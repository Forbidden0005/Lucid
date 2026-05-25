using ExplainMyPC.Intelligence.Models;
using ExplainMyPC.Models;

namespace ExplainMyPC.Intelligence.Rules.Performance;

/// <summary>
/// Fires when the CPU temperature exceeds safe operating thresholds.
///
/// Modern CPUs self-protect by reducing clock speed when they overheat
/// (thermal throttling). Users typically see this as mysterious stuttering or
/// loss of performance during sustained workloads, even though the CPU appears
/// to be running normally.
///
/// This rule only fires when the ThermalSampler reports IsAvailable=true.
/// On systems where thermal data is unavailable (some hypervisors, older
/// hardware without ACPI thermal zones) the rule is silently skipped.
///
/// Severity ladder (Celsius):
///   ≥ 95°C → High      (thermal throttling is active or imminent)
///   ≥ 85°C → Moderate  (sustained load at this temp will trigger throttling)
///   ≥ 78°C → Low       (warm but within operating range; worth monitoring)
///
/// Confidence is reduced slightly because the ACPI thermal zone may not
/// correspond precisely to the CPU package temperature.
/// </summary>
public sealed class ThermalThrottleRule : IRule
{
    public string RuleId => "perf.thermal.high";

    private const double CriticalTemp  = 95;
    private const double HighTemp      = 85;
    private const double ModerateTemp  = 78;

    public RuleResult? Evaluate(RuleContext ctx)
    {
        double temp = ctx.Current.CpuTemperatureCelsius;

        // Zero indicates the thermal sampler had no data — do not fire.
        if (temp <= 0 || temp < ModerateTemp) return null;

        var (severity, confidence) = temp switch
        {
            >= CriticalTemp => (IssueSeverity.High,     IssueConfidence.Medium),
            >= HighTemp     => (IssueSeverity.Moderate, IssueConfidence.Medium),
            _               => (IssueSeverity.Low,      IssueConfidence.Low),
        };

        string description = severity switch
        {
            IssueSeverity.High =>
                $"CPU temperature is {temp:F0}°C, which is high enough to trigger automatic slowdowns " +
                "(thermal throttling). Your CPU will reduce its speed to protect itself, causing " +
                "noticeable stuttering. Poor airflow or a dusty cooler are the most common causes.",
            IssueSeverity.Moderate =>
                $"CPU temperature is {temp:F0}°C. Under continued load this may cause the CPU to " +
                "throttle its speed to cool down. Ensuring good case airflow can help.",
            _ =>
                $"CPU temperature is {temp:F0}°C, which is within normal range but warmer than ideal. " +
                "No action is needed, but monitoring temperature during heavy workloads is worthwhile.",
        };

        return new RuleResult(
            RuleId:          RuleId,
            Title:           "CPU temperature is elevated",
            Description:     description,
            Severity:        severity,
            Confidence:      confidence,
            Category:        IssueCategory.Thermal,
            CanFix:          severity >= IssueSeverity.Moderate,
            Recommendations: [
                new Recommendation(
                    Title:           "Clean dust from CPU cooler and case vents",
                    Reason:          "Dust buildup is the leading cause of elevated temperatures in desktop and laptop PCs.",
                    ExpectedBenefit: "Can reduce temperatures by 10–20°C in heavily dusty systems.",
                    Risk:            RecommendationRisk.Low,
                    SupportsRollback: false),
                new Recommendation(
                    Title:           "Ensure case airflow is unobstructed",
                    Reason:          "Blocking intake vents (e.g. using a laptop on a soft surface) traps heat.",
                    ExpectedBenefit: "Immediate temperature reduction by improving air circulation.",
                    Risk:            RecommendationRisk.None,
                    SupportsRollback: false),
            ]);
    }
}
