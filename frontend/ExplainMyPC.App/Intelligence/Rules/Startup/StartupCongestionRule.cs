using ExplainMyPC.Intelligence.Models;
using ExplainMyPC.Models;

namespace ExplainMyPC.Intelligence.Rules.Startup;

/// <summary>
/// Fires when a large number of applications are configured to start with Windows.
///
/// Startup congestion is the most common cause of slow boot times on consumer
/// PCs. Each startup entry adds CPU, disk, and RAM overhead during the first
/// few minutes after login. Users rarely connect a slow-feeling morning boot
/// to the 20 apps that silently launched in the background.
///
/// The count threshold accounts for the fact that some startup entries are
/// essential (security software, system tray utilities the user actively uses).
/// The rule fires conservatively to avoid flagging reasonable configurations.
///
/// Thresholds (StartupAppCount from the StartupScanner):
///   ≥ 20 → High      (significant boot and memory impact)
///   ≥ 12 → Moderate  (noticeable boot delay)
///   ≥  8 → Low       (minor delay; worth reviewing)
///
/// Note: This rule only fires when StartupAppCount > 0. A count of 0 means
/// the StartupScanner has not run yet, so the rule stays silent.
/// </summary>
public sealed class StartupCongestionRule : IRule
{
    public string RuleId => "startup.congestion";

    private const int HighThreshold     = 20;
    private const int ModerateThreshold = 12;
    private const int LowThreshold      = 8;

    public RuleResult? Evaluate(RuleContext ctx)
    {
        int count = ctx.StartupAppCount;

        if (count < LowThreshold) return null;

        var (severity, confidence) = count switch
        {
            >= HighThreshold     => (IssueSeverity.High,     IssueConfidence.High),
            >= ModerateThreshold => (IssueSeverity.Moderate, IssueConfidence.High),
            _                    => (IssueSeverity.Low,      IssueConfidence.Medium),
        };

        string description = severity switch
        {
            IssueSeverity.High =>
                $"{count} applications are set to launch automatically when Windows starts. " +
                "This is a significant number and is likely adding several minutes to your boot time " +
                "while also increasing background memory and CPU usage throughout the day.",
            IssueSeverity.Moderate =>
                $"{count} applications launch automatically at startup. This is adding to your boot " +
                "time and increasing background resource usage. Many of these are likely apps that " +
                "do not need to start with Windows.",
            _ =>
                $"{count} applications are configured to start with Windows. " +
                "Reviewing which ones are actually needed could speed up your boot time.",
        };

        return new RuleResult(
            RuleId:          RuleId,
            Title:           $"{count} apps launch automatically at startup",
            Description:     description,
            Severity:        severity,
            Confidence:      confidence,
            Category:        IssueCategory.Startup,
            CanFix:          true,
            Recommendations: [
                new Recommendation(
                    Title:           "Review startup apps in Task Manager",
                    Reason:          "Many apps add themselves to startup without asking. Most are safe to disable.",
                    ExpectedBenefit: "Each disabled startup entry reduces boot time and background RAM usage.",
                    Risk:            RecommendationRisk.Low,
                    SupportsRollback: true),
                new Recommendation(
                    Title:           "Disable startup entries for apps you do not use daily",
                    Reason:          "Apps like Spotify, Discord, and OneDrive start with Windows by default but " +
                                     "can be launched manually when needed.",
                    ExpectedBenefit: "Can reduce boot time by 30–60 seconds on congested systems.",
                    Risk:            RecommendationRisk.Low,
                    SupportsRollback: true),
            ]);
    }
}
