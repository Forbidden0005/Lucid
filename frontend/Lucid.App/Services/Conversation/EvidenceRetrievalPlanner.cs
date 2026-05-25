namespace Lucid.Services.Conversation;

/// <summary>
/// Maps resolved conversation intents to the evidence sources that should
/// be consulted when composing a response.
///
/// Deterministic lookup — no async, no I/O. The planner tells the
/// <see cref="OperationalResponseComposer"/> which platform services to
/// read, keeping evidence access scoped and auditable.
///
/// Design constraints:
///   • Stateless pure function — no dependencies.
///   • Returns only sources genuinely needed to answer the intent.
///   • Avoids triggering expensive scans (storage, security) unless directly relevant.
/// </summary>
public sealed class EvidenceRetrievalPlanner
{
    /// <summary>
    /// Returns the ordered list of evidence sources the composer should consult
    /// for the given intent. Sources are listed from most to least relevant.
    /// </summary>
    public IReadOnlyList<EvidenceSource> Plan(ConversationIntent intent) => intent switch
    {
        // "Why slow?" — needs full picture: insights, narrative, telemetry, processes
        ConversationIntent.WhyIsSlow =>
        [
            EvidenceSource.InsightEngine,
            EvidenceSource.NarrativeEngine,
            EvidenceSource.Telemetry,
            EvidenceSource.EvidenceGraph,
        ],

        // "Why hot?" — thermal-focused
        ConversationIntent.WhyIsHot =>
        [
            EvidenceSource.InsightEngine,
            EvidenceSource.Telemetry,
        ],

        // "Why disk full?" — storage-focused; avoid triggering a new scan
        ConversationIntent.WhyIsDiskFull =>
        [
            EvidenceSource.InsightEngine,
            EvidenceSource.Telemetry,
            EvidenceSource.TimelineEvents,
        ],

        // "Why memory high?" — RAM + processes
        ConversationIntent.WhyIsMemoryHigh =>
        [
            EvidenceSource.InsightEngine,
            EvidenceSource.Telemetry,
            EvidenceSource.ProcessIntelligence,
        ],

        // "What changed?" — timeline, history, replay delta
        ConversationIntent.WhyDidSomethingChange or
        ConversationIntent.CompareChanges =>
        [
            EvidenceSource.TimelineEvents,
            EvidenceSource.OperationHistory,
            EvidenceSource.ReplayDelta,
        ],

        // Investigation — full evidence graph
        ConversationIntent.InvestigateProblem or
        ConversationIntent.OpenInvestigation =>
        [
            EvidenceSource.EvidenceGraph,
            EvidenceSource.InsightEngine,
            EvidenceSource.TimelineEvents,
        ],

        // Navigation intents — minimal evidence needed; just route the user
        ConversationIntent.OpenRepairs or
        ConversationIntent.OpenStorage or
        ConversationIntent.OpenTimeline or
        ConversationIntent.OpenReplay or
        ConversationIntent.OpenHistorical =>
        [
            EvidenceSource.InsightEngine,   // quick count for context
        ],

        // Workflow guidance
        ConversationIntent.StartGuidedWorkflow =>
        [
            EvidenceSource.EvidenceGraph,
            EvidenceSource.InsightEngine,
        ],

        // Recommendation explanation
        ConversationIntent.ExplainRecommendation =>
        [
            EvidenceSource.InsightEngine,
            EvidenceSource.NarrativeEngine,
        ],

        // Timeline review
        ConversationIntent.ReviewTimeline =>
        [
            EvidenceSource.TimelineEvents,
            EvidenceSource.OperationHistory,
        ],

        // Context-aware intents
        ConversationIntent.FindRecentDownloads or
        ConversationIntent.ReviewClipboardFiles or
        ConversationIntent.ExplainCurrentContext =>
        [
            EvidenceSource.DesktopContext,
            EvidenceSource.InsightEngine,
        ],

        // Status / general / greeting / help
        ConversationIntent.SummarizeCurrentState or
        ConversationIntent.Unknown =>
        [
            EvidenceSource.NarrativeEngine,
            EvidenceSource.InsightEngine,
            EvidenceSource.Telemetry,
        ],

        // Help and greeting — no platform evidence needed
        ConversationIntent.Greeting or
        ConversationIntent.Help => [],

        _ =>
        [
            EvidenceSource.InsightEngine,
            EvidenceSource.NarrativeEngine,
        ],
    };

    /// <summary>
    /// Returns a display-friendly comma-separated list of source names
    /// so the UI can show which evidence was used in the response badge.
    /// </summary>
    public static string FormatSourceList(IReadOnlyList<EvidenceSource> sources) =>
        sources.Count == 0
            ? "No external sources"
            : string.Join(", ", sources.Select(FormatSource));

    private static string FormatSource(EvidenceSource s) => s switch
    {
        EvidenceSource.Telemetry           => "Live telemetry",
        EvidenceSource.InsightEngine       => "Insight engine",
        EvidenceSource.TimelineEvents      => "Timeline",
        EvidenceSource.EvidenceGraph       => "Evidence graph",
        EvidenceSource.ReplayDelta         => "Replay delta",
        EvidenceSource.OperationHistory    => "Operation history",
        EvidenceSource.DesktopContext      => "Desktop context",
        EvidenceSource.StorageAnalysis     => "Storage analysis",
        EvidenceSource.ProcessIntelligence => "Process intelligence",
        EvidenceSource.NarrativeEngine     => "Narrative engine",
        _                                  => s.ToString(),
    };
}
