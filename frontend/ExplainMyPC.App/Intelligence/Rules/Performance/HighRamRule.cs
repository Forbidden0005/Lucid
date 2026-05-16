using ExplainMyPC.Intelligence.Models;
using ExplainMyPC.Models;

namespace ExplainMyPC.Intelligence.Rules.Performance;

/// <summary>
/// Fires when RAM utilization is sustained above a threshold.
///
/// RAM pressure is more persistent than CPU spikes — once RAM fills,
/// it stays full until applications close. A 3-reading window is
/// sufficient because RAM usage changes slowly.
///
/// Severity ladder:
///   ≥ 92% avg → High      (paging to disk is almost certain)
///   ≥ 82% avg → Moderate  (limited headroom; multitasking suffers)
///   ≥ 72% avg → Low       (adequate but tight)
///
/// Note: The MemoryPressureRule handles the correlated case where high RAM
/// coincides with high disk I/O (active paging). This rule covers the simpler
/// "RAM is just full" case regardless of disk activity.
/// </summary>
public sealed class HighRamRule : IRule
{
    public string RuleId => "perf.ram.high";

    private const double HighThreshold     = 92;
    private const double ModerateThreshold = 82;
    private const double LowThreshold      = 72;

    public RuleResult? Evaluate(RuleContext ctx)
    {
        double avg = ctx.RamAverage(lastN: 3);

        if (avg < LowThreshold) return null;

        long usedGb  = ctx.Current.RamUsedMb  / 1024;
        long totalGb = ctx.Current.RamTotalMb  / 1024;

        var (severity, confidence) = avg switch
        {
            >= HighThreshold     => (IssueSeverity.High,     IssueConfidence.High),
            >= ModerateThreshold => (IssueSeverity.Moderate, IssueConfidence.Medium),
            _                    => (IssueSeverity.Low,      IssueConfidence.Low),
        };

        string description = severity switch
        {
            IssueSeverity.High =>
                $"Your system is using {usedGb} GB of its {totalGb} GB of RAM ({avg:F0}%). " +
                "Windows is likely using the disk as overflow memory (paging), which is significantly slower than RAM.",
            IssueSeverity.Moderate =>
                $"RAM usage is at {avg:F0}% ({usedGb} GB of {totalGb} GB used). " +
                "There is limited headroom for opening new applications without slowing down.",
            _ =>
                $"RAM usage is at {avg:F0}% ({usedGb} GB of {totalGb} GB used). " +
                "Performance is fine, but closing unused applications would free up headroom.",
        };

        return new RuleResult(
            RuleId:          RuleId,
            Title:           "Memory usage is high",
            Description:     description,
            Severity:        severity,
            Confidence:      confidence,
            Category:        IssueCategory.Memory,
            CanFix:          true,
            Recommendations: [
                new Recommendation(
                    Title:           "Close unused applications and browser tabs",
                    Reason:          "Each open browser tab and application consumes memory.",
                    ExpectedBenefit: $"Closing unused apps could free several hundred MB to GB of RAM.",
                    Risk:            RecommendationRisk.None,
                    SupportsRollback: false),
                new Recommendation(
                    Title:           "Review startup applications that run in the background",
                    Reason:          "Many apps start with Windows and consume RAM even when not actively used.",
                    ExpectedBenefit: "Disabling background startup apps can reduce baseline RAM usage.",
                    Risk:            RecommendationRisk.Low,
                    SupportsRollback: true),
            ]);
    }
}
