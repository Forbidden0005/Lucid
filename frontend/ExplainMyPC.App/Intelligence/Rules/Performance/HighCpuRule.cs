using ExplainMyPC.Intelligence.Models;
using ExplainMyPC.Models;

namespace ExplainMyPC.Intelligence.Rules.Performance;

/// <summary>
/// Fires when CPU utilization is sustained above a threshold.
///
/// Uses a 5-reading rolling average rather than the instantaneous value to
/// avoid false positives from brief bursts (e.g. antivirus scan starting,
/// app launching). A momentary spike to 100% is normal; sustained load above
/// 80% indicates genuine pressure.
///
/// Severity ladder:
///   ≥ 90% avg → High      (noticeable lag, system is struggling)
///   ≥ 80% avg → Moderate  (degraded responsiveness)
///   ≥ 70% avg → Low       (worth monitoring)
/// </summary>
public sealed class HighCpuRule : IRule
{
    public string RuleId => "perf.cpu.high";

    private const double HighThreshold     = 90;
    private const double ModerateThreshold = 80;
    private const double LowThreshold      = 70;

    public RuleResult? Evaluate(RuleContext ctx)
    {
        double avg = ctx.CpuAverage(lastN: 5);

        if (avg < LowThreshold) return null;

        var (severity, confidence) = avg switch
        {
            >= HighThreshold     => (IssueSeverity.High,     IssueConfidence.High),
            >= ModerateThreshold => (IssueSeverity.Moderate, IssueConfidence.Medium),
            _                    => (IssueSeverity.Low,      IssueConfidence.Low),
        };

        string description = severity switch
        {
            IssueSeverity.High =>
                $"Your CPU has been running at {avg:F0}% capacity, which is causing noticeable slowness. " +
                "One or more applications may be consuming excessive processing power.",
            IssueSeverity.Moderate =>
                $"Your CPU is working at {avg:F0}% capacity on average. " +
                "This may reduce responsiveness when opening new applications or switching tasks.",
            _ =>
                $"Your CPU is running at {avg:F0}% capacity. Performance is acceptable but worth monitoring.",
        };

        return new RuleResult(
            RuleId:          RuleId,
            Title:           "CPU usage is elevated",
            Description:     description,
            Severity:        severity,
            Confidence:      confidence,
            Category:        IssueCategory.Performance,
            CanFix:          true,
            Recommendations: [
                new Recommendation(
                    Title:           "Identify high-CPU processes",
                    Reason:          "One application is likely responsible for most of the load.",
                    ExpectedBenefit: "Closing or restarting the offending app should immediately reduce CPU usage.",
                    Risk:            RecommendationRisk.None,
                    SupportsRollback: false),
                new Recommendation(
                    Title:           "Check for runaway background services",
                    Reason:          "Antivirus scans, update processes, and indexing can temporarily spike CPU.",
                    ExpectedBenefit: "If a scheduled task is causing this, it will resolve on its own.",
                    Risk:            RecommendationRisk.None,
                    SupportsRollback: false),
            ]);
    }
}
