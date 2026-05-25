using Lucid.Services.Companion;
using Lucid.Services.Intelligence;

namespace Lucid.Services.Conversation;

/// <summary>
/// Surfaces platform workflows conversationally — as suggestions and navigation
/// guidance, never as auto-executed actions.
///
/// Given a resolved intent, the bridge returns a list of <see cref="SuggestedAction"/>
/// objects the companion UI renders as tappable navigation chips.
/// The user always decides whether to proceed — this bridge only recommends.
///
/// Design constraints:
///   • Stateless — can be called from any thread.
///   • No workflow execution — navigation only.
///   • Suggestions adapt to which active insights are present.
/// </summary>
public sealed class WorkflowConversationBridge
{
    private readonly ISystemInsightEngine _insights;

    public WorkflowConversationBridge(ISystemInsightEngine insights)
    {
        _insights = insights;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns suggested navigation actions for the given intent.
    /// May return an empty list when no workflow is relevant.
    /// </summary>
    public IReadOnlyList<SuggestedAction> GetSuggestedActions(ConversationIntent intent)
    {
        var actions = new List<SuggestedAction>();
        var insights = _insights.CurrentInsights;

        switch (intent)
        {
            case ConversationIntent.InvestigateProblem:
            case ConversationIntent.OpenInvestigation:
                actions.Add(NavigateTo("investigation", "Investigate", "",
                    NavigationTarget.Investigation,
                    "Open the Investigation workspace with root-cause analysis"));
                if (HasWarnings(insights))
                    actions.Add(NavigateTo("insights", "View Insights", "",
                        NavigationTarget.Insights,
                        "Browse active insight findings"));
                break;

            case ConversationIntent.WhyIsSlow:
            case ConversationIntent.WhyIsMemoryHigh:
                actions.Add(NavigateTo("processes", "View Processes", "",
                    NavigationTarget.Processes,
                    "See which processes are consuming resources"));
                actions.Add(NavigateTo("investigation", "Investigate", "",
                    NavigationTarget.Investigation,
                    "Run root-cause analysis"));
                break;

            case ConversationIntent.WhyIsHot:
                actions.Add(NavigateTo("processes", "View Processes", "",
                    NavigationTarget.Processes,
                    "Identify thermally intensive processes"));
                actions.Add(NavigateTo("insights", "View Insights", "",
                    NavigationTarget.Insights,
                    "See thermal findings"));
                break;

            case ConversationIntent.WhyIsDiskFull:
            case ConversationIntent.OpenStorage:
            case ConversationIntent.FindRecentDownloads:
                actions.Add(NavigateTo("storage", "Analyze Storage", "",
                    NavigationTarget.Storage,
                    "Find large files, duplicates, and cleanup opportunities"));
                break;

            case ConversationIntent.OpenRepairs:
            case ConversationIntent.StartGuidedWorkflow:
                actions.Add(NavigateTo("repairs", "Open Repairs", "",
                    NavigationTarget.Repairs,
                    "Browse guided repair and cleanup workflows"));
                if (HasWarnings(insights))
                    actions.Add(NavigateTo("investigation", "Investigate First", "",
                        NavigationTarget.Investigation,
                        "Identify root causes before running repairs"));
                break;

            case ConversationIntent.WhyDidSomethingChange:
            case ConversationIntent.CompareChanges:
            case ConversationIntent.OpenReplay:
                actions.Add(NavigateTo("replay", "Open Replay", "",
                    NavigationTarget.Replay,
                    "Browse the operational replay system"));
                actions.Add(NavigateTo("timeline", "View Timeline", "",
                    NavigationTarget.Timeline,
                    "See the full event timeline"));
                break;

            case ConversationIntent.ReviewTimeline:
            case ConversationIntent.OpenTimeline:
                actions.Add(NavigateTo("timeline", "View Timeline", "",
                    NavigationTarget.Timeline,
                    "Browse the operational event timeline"));
                break;

            case ConversationIntent.OpenHistorical:
                actions.Add(NavigateTo("historical", "Historical Analysis", "",
                    NavigationTarget.Historical,
                    "View long-term health trends and analytics"));
                break;

            case ConversationIntent.SummarizeCurrentState:
            case ConversationIntent.Unknown:
                if (HasWarnings(insights))
                    actions.Add(NavigateTo("investigation", "Investigate", "",
                        NavigationTarget.Investigation,
                        "Run root-cause analysis on active findings"));
                actions.Add(NavigateTo("explain", "Explain My PC", "",
                    NavigationTarget.Explain,
                    "Get a full plain-English explanation of your system"));
                break;

            case ConversationIntent.ExplainCurrentContext:
            case ConversationIntent.ReviewClipboardFiles:
                actions.Add(NavigateTo("storage", "Analyze Storage", "",
                    NavigationTarget.Storage,
                    "Review and organize files"));
                break;

            case ConversationIntent.ExplainRecommendation:
                actions.Add(NavigateTo("insights", "View Insights", "",
                    NavigationTarget.Insights,
                    "See all current recommendations"));
                break;

            // ── Phase 17D: automation launch chips ────────────────────────────

            case ConversationIntent.LaunchTaskManager:
                actions.Add(AutomationAction("task-manager", "Open Task Manager",
                    "Launch Task Manager to see process CPU/memory usage"));
                actions.Add(NavigateTo("processes", "Lucid Processes", "",
                    NavigationTarget.Processes,
                    "View process intelligence in Lucid"));
                break;

            case ConversationIntent.LaunchDeviceManager:
                actions.Add(AutomationAction("device-manager", "Open Device Manager",
                    "Launch Device Manager to check hardware and driver status"));
                break;

            case ConversationIntent.LaunchEventViewer:
                actions.Add(AutomationAction("event-viewer", "Open Event Viewer",
                    "Launch Event Viewer to review Windows system logs"));
                actions.Add(NavigateTo("timeline", "View Timeline", "",
                    NavigationTarget.Timeline,
                    "See Lucid operational event timeline"));
                break;

            case ConversationIntent.LaunchStorageSettings:
                actions.Add(AutomationAction("storage-settings", "Open Storage Settings",
                    "Launch Windows Storage settings"));
                actions.Add(NavigateTo("storage", "Analyze Storage", "",
                    NavigationTarget.Storage,
                    "Deep storage analysis in Lucid"));
                break;

            case ConversationIntent.LaunchStartupApps:
                actions.Add(AutomationAction("startup-apps", "Open Startup Apps",
                    "Launch Startup Apps settings to manage login items"));
                break;

            case ConversationIntent.LaunchWindowsSecurity:
                actions.Add(AutomationAction("windows-security", "Open Windows Security",
                    "Launch Windows Security / Defender"));
                actions.Add(NavigateTo("security", "Lucid Security", "",
                    NavigationTarget.Security,
                    "View Lucid security intelligence"));
                break;

            case ConversationIntent.OpenDownloadsFolder:
                actions.Add(AutomationAction("downloads", "Open Downloads",
                    "Open Downloads folder in File Explorer"));
                actions.Add(NavigateTo("storage", "Analyze Storage", "",
                    NavigationTarget.Storage,
                    "Find large files and duplicates"));
                break;

            case ConversationIntent.OpenDocumentsFolder:
                actions.Add(AutomationAction("documents", "Open Documents",
                    "Open Documents folder in File Explorer"));
                break;

            case ConversationIntent.LaunchNetworkSettings:
                actions.Add(AutomationAction("network-settings", "Open Network Settings",
                    "Launch Windows Network settings"));
                break;

            case ConversationIntent.LaunchWindowsUpdate:
                actions.Add(AutomationAction("windows-update", "Open Windows Update",
                    "Launch Windows Update settings"));
                break;
        }

        return actions;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static bool HasWarnings(IReadOnlyList<SystemInsight> insights)
        => insights.Any(i => i.Severity >= InsightSeverity.Warning);

    private static SuggestedAction NavigateTo(
        string id, string label, string glyph,
        NavigationTarget target, string? description = null) =>
        new()
        {
            Id          = id,
            Label       = label,
            Glyph       = glyph,
            Target      = target,
            Description = description,
        };

    /// <summary>
    /// Creates an automation chip — looks like a navigation chip but carries
    /// a special NavigationTag that the CompanionOverlayWindow interprets as
    /// an automation launch request (prefix "automation:").
    /// </summary>
    private static SuggestedAction AutomationAction(
        string target, string label, string? description = null) =>
        new()
        {
            Id            = $"auto-{target}",
            Label         = label,
            Glyph         = "",
            Target        = NavigationTarget.None,
            NavigationTag = $"automation:{target}",
            Description   = description,
        };
}
