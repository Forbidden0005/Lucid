using ExplainMyPC.Services.Companion;
using ExplainMyPC.Services.DesktopContext;
using ExplainMyPC.Services.Intelligence;
using ExplainMyPC.Services.Narrative;
using ExplainMyPC.Services.Reasoning;
using ExplainMyPC.Services.Timeline;

namespace ExplainMyPC.Services.Conversation;

/// <summary>
/// Orchestrates the Phase 17C operational conversation layer.
///
/// Implements both:
///   <see cref="IOperationalConversationService"/> — the rich Phase 17C interface
///   <see cref="IOperationalConversationEngine"/> — the existing interface for backward compat
///
/// Processing pipeline per query:
///   1. ConversationIntentResolver  — deterministic keyword matching → IntentMatch
///   2. EvidenceRetrievalPlanner    — maps intent to evidence sources to consult
///   3. OperationalResponseComposer — builds the response from live service data
///   4. WorkflowConversationBridge  — adds navigation action suggestions
///   5. ContextualSuggestionEngine  — surfaces desktop-context suggestions on demand
///
/// Threading:
///   ProcessQueryAsync runs the quick sync path (intent resolution + planning)
///   on the caller's thread, then dispatches the compose step to Task.Run only
///   when async graph building is needed. Most responses complete synchronously.
///
/// Performance target: &lt;150ms for non-investigation intents.
/// </summary>
public sealed class OperationalConversationService
    : IOperationalConversationService, IOperationalConversationEngine
{
    // ── Sub-services ───────────────────────────────────────────────────────────

    private readonly ConversationIntentResolver   _intentResolver;
    private readonly EvidenceRetrievalPlanner     _planner;
    private readonly OperationalResponseComposer  _composer;
    private readonly ContextualSuggestionEngine   _contextEngine;

    // ── Constructor ────────────────────────────────────────────────────────────

    public OperationalConversationService(
        ISystemInsightEngine        insights,
        IOperationalNarrativeEngine narrative,
        ITimelineAggregationService timeline,
        IOperationalEvidenceGraph   evidenceGraph,
        IRootCauseAnalysisEngine    rootCauseEngine,
        IDesktopContextService      desktopContext)
    {
        _intentResolver = new ConversationIntentResolver();
        _planner        = new EvidenceRetrievalPlanner();

        var replayBridge   = new ReplayConversationBridge(timeline);
        var workflowBridge = new WorkflowConversationBridge(insights);

        _composer = new OperationalResponseComposer(
            insights, narrative, timeline,
            evidenceGraph, rootCauseEngine, desktopContext,
            replayBridge, workflowBridge, _planner);

        _contextEngine = new ContextualSuggestionEngine(desktopContext);
    }

    // ── IOperationalConversationService ───────────────────────────────────────

    /// <summary>
    /// Processes a rich <see cref="OperationalPrompt"/> and returns a structured
    /// <see cref="OperationalResponse"/> with evidence, confidence, and suggested actions.
    /// </summary>
    public async Task<OperationalResponse> ProcessQueryAsync(
        OperationalPrompt prompt, CancellationToken ct = default)
    {
        var match = _intentResolver.Resolve(prompt.Text, prompt);
        return await _composer.ComposeAsync(match, prompt, ct).ConfigureAwait(false);
    }

    /// <summary>Returns current contextual suggestions from the desktop context layer.</summary>
    public IReadOnlyList<ContextualSuggestion> GetContextualSuggestions()
        => _contextEngine.GetSuggestions();

    // ── IOperationalConversationEngine (backward compat) ──────────────────────

    /// <summary>
    /// Legacy interface method — wraps the richer ProcessQueryAsync path.
    /// Converts the desktop context snapshot into an OperationalPrompt and
    /// converts the OperationalResponse back into a CompanionMessage.
    /// </summary>
    public async Task<CompanionMessage> ProcessQueryAsync(
        string query, CancellationToken ct = default)
    {
        var prompt = new OperationalPrompt { Text = query };
        var response = await ProcessQueryAsync(prompt, ct).ConfigureAwait(false);
        return ResponseToMessage(response);
    }

    // ── Conversion helper ──────────────────────────────────────────────────────

    /// <summary>
    /// Converts an <see cref="OperationalResponse"/> to a <see cref="CompanionMessage"/>
    /// suitable for the companion message list.
    /// </summary>
    public static CompanionMessage ResponseToMessage(OperationalResponse response) => new()
    {
        Id                      = Guid.NewGuid().ToString("N")[..8],
        Role                    = CompanionMessageRole.System,
        Text                    = response.Text,
        Timestamp               = DateTimeOffset.Now,
        Category                = response.Category,
        SuggestedActions        = response.SuggestedActions,
        EvidenceItems           = response.Evidence,
        ConfidenceDisplay       = response.Confidence.Percent > 0
                                  ? response.Confidence.Display : null,
        UncertaintyNote         = response.UncertaintyNote,
    };
}
