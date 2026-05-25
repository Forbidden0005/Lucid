using ExplainMyPC.Intelligence.Models;
using ExplainMyPC.Models;

namespace ExplainMyPC.Intelligence.Rules.Performance;

/// <summary>
/// Composite rule: fires when high RAM usage coincides with elevated disk I/O.
///
/// This correlation is a strong signal that Windows is actively paging — reading
/// and writing the page file because physical RAM is exhausted. Paging is 10–100×
/// slower than RAM, and users experience it as severe, unexplained slowness that
/// seems unrelated to what they are doing.
///
/// Distinguishing from independent RAM and Disk rules:
///   • HighRamRule fires on memory pressure alone.
///   • HighDiskIoRule fires on I/O throughput alone.
///   • This rule fires only when BOTH are elevated simultaneously, which is the
///     specific pattern of active paging. It produces a more informative
///     description that names paging as the root cause.
///
/// Thresholds:
///   RAM ≥ 78% AND DiskIo ≥ 25% avg → Moderate (paging likely)
///   RAM ≥ 85% AND DiskIo ≥ 40% avg → High     (heavy paging confirmed)
/// </summary>
public sealed class MemoryPressureRule : IRule
{
    public string RuleId => "perf.memory.pressure";

    private const double RamHighThreshold     = 85;
    private const double DiskIoHighThreshold  = 40;
    private const double RamModThreshold      = 78;
    private const double DiskIoModThreshold   = 25;

    public RuleResult? Evaluate(RuleContext ctx)
    {
        double avgRam    = ctx.RamAverage(lastN: 5);
        double avgDiskIo = ctx.DiskIoAverage(lastN: 5);

        bool isHigh     = avgRam >= RamHighThreshold    && avgDiskIo >= DiskIoHighThreshold;
        bool isModerate = avgRam >= RamModThreshold     && avgDiskIo >= DiskIoModThreshold;

        if (!isHigh && !isModerate) return null;

        var severity   = isHigh ? IssueSeverity.High     : IssueSeverity.Moderate;
        var confidence = isHigh ? IssueConfidence.High   : IssueConfidence.Medium;

        long usedGb  = ctx.Current.RamUsedMb / 1024;
        long totalGb = ctx.Current.RamTotalMb / 1024;

        string description = isHigh
            ? $"Your system has used up its physical RAM ({usedGb} GB of {totalGb} GB) and is " +
              "actively moving data to the slower page file on your disk. This is the most common cause " +
              "of severe, unpredictable slowness — everything feels sluggish because disk access is " +
              "10–100× slower than RAM."
            : $"RAM usage is at {avgRam:F0}% ({usedGb} GB of {totalGb} GB) and your disk is also " +
              "active. Windows may be using the page file. If slowness continues, closing some " +
              "applications should help.";

        return new RuleResult(
            RuleId:          RuleId,
            Title:           "Memory pressure is causing disk activity",
            Description:     description,
            Severity:        severity,
            Confidence:      confidence,
            Category:        IssueCategory.Memory,
            CanFix:          true,
            Recommendations: [
                new Recommendation(
                    Title:           "Close memory-intensive applications",
                    Reason:          "Reducing RAM usage stops Windows from paging to disk.",
                    ExpectedBenefit: "System responsiveness should improve within seconds of freeing RAM.",
                    Risk:            RecommendationRisk.None,
                    SupportsRollback: false),
                new Recommendation(
                    Title:           "Consider upgrading RAM",
                    Reason:          $"Your system regularly uses close to all {totalGb} GB. Adding more RAM " +
                                     "eliminates paging entirely for typical workloads.",
                    ExpectedBenefit: "Eliminates page file pressure and improves sustained performance.",
                    Risk:            RecommendationRisk.None,
                    SupportsRollback: false),
            ]);
    }
}
