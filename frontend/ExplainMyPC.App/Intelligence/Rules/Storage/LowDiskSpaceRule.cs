using ExplainMyPC.Intelligence.Models;
using ExplainMyPC.Models;

namespace ExplainMyPC.Intelligence.Rules.Storage;

/// <summary>
/// Fires when the total disk fill ratio exceeds safe operating levels.
///
/// Low disk space causes visible symptoms that users rarely connect to storage:
///   • Windows cannot create restore points (safety net disappears silently)
///   • Hibernate and sleep may fail because the hiberfil.sys cannot be written
///   • Some SSDs suffer write performance degradation above 90% fill (reduced
///     space for wear-leveling and garbage collection)
///   • Applications that write temp files fail unpredictably
///
/// Severity ladder (by fill ratio):
///   ≥ 97% → High      (< ~15 GB free on a 512 GB drive — critical operations at risk)
///   ≥ 90% → Moderate  (< ~51 GB free — SSD performance may degrade)
///   ≥ 80% → Low       (< ~102 GB free — worth addressing proactively)
///
/// The rule also fires if free space drops below an absolute minimum (10 GB)
/// regardless of percentage, because a small drive that hits 97% of 128 GB
/// only has ~4 GB free — genuinely critical.
/// </summary>
public sealed class LowDiskSpaceRule : IRule
{
    public string RuleId => "storage.disk.low_space";

    private const double HighRatio     = 0.97;
    private const double ModerateRatio = 0.90;
    private const double LowRatio      = 0.80;
    private const long   AbsoluteMinFreeGb = 10;

    public RuleResult? Evaluate(RuleContext ctx)
    {
        if (ctx.Current.DiskTotalGb <= 0) return null;

        double fill    = ctx.DiskFillRatio;
        long   freeGb  = ctx.DiskFreeGb;
        long   totalGb = ctx.Current.DiskTotalGb;
        long   usedGb  = ctx.Current.DiskUsedGb;

        bool isCriticallyLow = freeGb < AbsoluteMinFreeGb;

        if (!isCriticallyLow && fill < LowRatio) return null;

        var (severity, confidence) = (isCriticallyLow || fill >= HighRatio)
            ? (IssueSeverity.High,     IssueConfidence.VeryHigh)
            : fill >= ModerateRatio
                ? (IssueSeverity.Moderate, IssueConfidence.High)
                : (IssueSeverity.Low,      IssueConfidence.High);

        string fillPct = $"{fill * 100:F0}%";
        string description = severity switch
        {
            IssueSeverity.High =>
                $"Your disk is {fillPct} full with only {freeGb} GB remaining out of {totalGb} GB. " +
                "At this level, Windows may be unable to create restore points, and system performance " +
                "may degrade. Freeing space should be a priority.",
            IssueSeverity.Moderate =>
                $"Your disk is {fillPct} full ({usedGb} GB used of {totalGb} GB). " +
                "SSDs can lose write performance when this full, and Windows has limited space for " +
                "temporary files and system operations.",
            _ =>
                $"Your disk is {fillPct} full ({usedGb} GB used of {totalGb} GB). " +
                "Performance is unaffected now, but clearing some space would provide more headroom.",
        };

        return new RuleResult(
            RuleId:          RuleId,
            Title:           "Disk space is running low",
            Description:     description,
            Severity:        severity,
            Confidence:      confidence,
            Category:        IssueCategory.Storage,
            CanFix:          true,
            Recommendations: [
                new Recommendation(
                    Title:           "Run Disk Cleanup to remove temporary files",
                    Reason:          "Windows accumulates temporary files, update caches, and thumbnails over time.",
                    ExpectedBenefit: "Typically recovers 1–20 GB depending on how long since last cleanup.",
                    Risk:            RecommendationRisk.Low,
                    SupportsRollback: false),
                new Recommendation(
                    Title:           "Review the Downloads folder for large files",
                    Reason:          "Downloaded installers and media files are a common source of wasted space.",
                    ExpectedBenefit: "Can recover significant space quickly without risk to the system.",
                    Risk:            RecommendationRisk.None,
                    SupportsRollback: false),
                new Recommendation(
                    Title:           "Uninstall applications you no longer use",
                    Reason:          "Installed software — especially games — can occupy tens of gigabytes.",
                    ExpectedBenefit: "Permanently reclaims space for more important use.",
                    Risk:            RecommendationRisk.Medium,
                    SupportsRollback: true),
            ]);
    }
}
