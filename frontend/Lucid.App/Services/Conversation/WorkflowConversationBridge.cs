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
                actions.Add(NavigateTo("investigation", "Investigate", "",
                    NavigationTarget.Investigation,
                    "Open the Investigation workspace with root-cause analysis"));
                if (HasWarnings(insights))
                    actions.Add(NavigateTo("insights", "View Insights", "",
                        NavigationTarget.Insights,
                        "Browse active insight findings"));
                break;

            case ConversationIntent.WhyIsSlow:
            case ConversationIntent.WhyIsMemoryHigh:
                actions.Add(NavigateTo("processes", "View Processes", "",
                    NavigationTarget.Processes,
                    "See which processes are consuming resources"));
                actions.Add(NavigateTo("investigation", "Investigate", "",
                    NavigationTarget.Investigation,
                    "Run root-cause analysis"));
                break;

            case ConversationIntent.WhyIsHot:
                actions.Add(NavigateTo("processes", "View Processes", "",
                    NavigationTarget.Processes,
                    "Identify thermally intensive processes"));
                actions.Add(NavigateTo("insights", "View Insights", "",
                    NavigationTarget.Insights,
                    "See thermal findings"));
                break;

            case ConversationIntent.WhyIsDiskFull:
            case ConversationIntent.OpenStorage:
            case ConversationIntent.FindRecentDownloads:
                actions.Add(NavigateTo("storage", "Analyze Storage", "",
                    NavigationTarget.Storage,
                    "Find large files, duplicates, and cleanup opportunities"));
                break;

            case ConversationIntent.OpenRepairs:
            case ConversationIntent.StartGuidedWorkflow:
                actions.Add(NavigateTo("repairs", "Open Repairs", "",
                    NavigationTarget.Repairs,
                    "Browse guided repair and cleanup workflows"));
                if (HasWarnings(insights))
                    actions.Add(NavigateTo("investigation", "Investigate First", "",
                        NavigationTarget.Investigation,
                        "Identify root causes before running repairs"));
                break;

            case ConversationIntent.WhyDidSomethingChange:
            case ConversationIntent.CompareChanges:
            case ConversationIntent.OpenReplay:
                actions.Add(NavigateTo("replay", "Open Replay", "",
                    NavigationTarget.Replay,
                    "Browse the operational replay system"));
                actions.Add(NavigateTo("timeline", "View Timeline", "",
                    NavigationTarget.Timeline,
                    "See the full event timeline"));
                break;

            case ConversationIntent.ReviewTimeline:
            case ConversationIntent.OpenTimeline:
                actions.Add(NavigateTo("timeline", "View Timeline", "",
                    NavigationTarget.Timeline,
                    "Browse the operational event timeline"));
                break;

            case ConversationIntent.OpenHistorical:
                actions.Add(NavigateTo("historical", "Historical Analysis", "",
                    NavigationTarget.Historical,
                    "View long-term health trends and analytics"));
                break;

            case ConversationIntent.SummarizeCurrentState:
            case ConversationIntent.Unknown:
                // Always offer investigation when there are active warnings
                if (HasWarnings(insights))
                    actions.Add(NavigateTo("investigation", "Investigate", "",
                        NavigationTarget.Investigation,
                        "Run root-cause analysis on active findings"));
                actions.Add(NavigateTo("explain", "Explain My PC", "",
                    NavigationTarget.Explain,
                    "Get a full plain-English explanation of your system"));
                break;

            case ConversationIntent.ExplainCurrentContext:
            case ConversationIntent.ReviewClipboardFiles:
                actions.Add(NavigateTo("storage", "Analyze Storage", "",
                    NavigationTarget.Storage,
                    "Review and organize files"));
                break;

            case ConversationIntent.ExplainRecommendation:
                actions.Add(NavigateTo("insights", "View Insights", "",
                    NavigationTarget.Insights,
                    "See all current recommendations"));
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
}
