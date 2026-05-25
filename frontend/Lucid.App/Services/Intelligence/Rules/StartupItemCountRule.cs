using Lucid.Helpers;
using Lucid.Services.Startup;
using Lucid.Services.Telemetry;

namespace Lucid.Services.Intelligence.Rules;

/// <summary>
/// Fires when the user has an unusually large number of startup applications
/// (≥ 8 enabled entries across registry Run keys and the Startup folder).
///
/// Windows Task Manager's own "Startup" tab flags "High", "Medium", and "Low"
/// impact categories — users rarely review this list. The more apps are
/// configured to start at sign-in, the longer the sign-in takes and the more
/// RAM and CPU are consumed in the minutes after login while each app initializes.
///
/// This rule does not flag specific apps; <see cref="HighImpactStartupAppsRule"/>
/// handles name-specific warnings. This rule fires purely on count.
///
/// Severity: Recommendation (always — a large list may be intentional).
/// ID: startup.many-items
/// </summary>
public sealed class StartupItemCountRule : IInsightRule
{
    private const int ManyItemsThreshold = 8;

    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.startup.open-startup-apps",
            Title:                "Review Startup Apps",
            Description:          "Open the Windows Startup Apps settings page to review and disable applications you don't need to launch automatically at sign-in.",
            Impact:               ActionImpact.Moderate,
            Effort:               ActionEffort.FewMinutes,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),
    ];

    public string RuleId => "startup.many-items";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        // Wait until the first StartupSampler refresh cycle completes.
        if (current.StartupEntries.Count == 0)
            return null;

        var count = current.StartupEntries.Count;
        if (count < ManyItemsThreshold)
            return null;

        return new SystemInsight(
            Id:       RuleId,
            Severity: InsightSeverity.Recommendation,
            Title:    "Many Apps Start at Login",
            Detail:   $"You have {count} applications configured to launch automatically when you sign in. " +
                      $"Each startup app adds to your sign-in time and holds RAM and CPU even when you're not using it. " +
                      $"Disabling apps you don't need immediately after login is one of the most effective ways to speed up startup.",
            ActionHint:         "Open Startup Apps to review your list and disable entries you don't need right away.",
            DetectedAt:         DateTimeOffset.Now,
            ConfidencePercent:  100,
            RecommendedActions: s_actions);
    }
}
