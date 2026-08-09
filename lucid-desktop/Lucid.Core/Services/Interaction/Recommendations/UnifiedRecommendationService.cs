using Lucid.Services.Intelligence;
using Lucid.Services.Intelligence.Arbitration;
using Lucid.Services.Interaction.Language;
using Lucid.Services.Learning;

namespace Lucid.Services.Interaction.Recommendations;

/// <summary>
/// Transforms arbitration output into fully-rendered <see cref="UnifiedRecommendation"/>
/// records, adding lifecycle state, fatigue transparency, and explanation text.
///
/// This service is the single point where arbitration clusters become the
/// "recommendation surface" — the unified list that all ViewModels consume.
///
/// Key responsibilities:
///   - Wrap each <see cref="RecommendationCluster"/> with formatted display text
///   - Apply lifecycle state from context suppression and deferred tracking
///   - Attach fatigue transparency notes from <see cref="IAlertFatigueManager"/>
///   - Produce a stable, priority-sorted list
///
/// Thread-safe: reads from immutable inputs; no mutable state.
/// </summary>
public sealed class UnifiedRecommendationService
{
    private readonly IRecommendationArbitrator  _arbitrator;
    private readonly IAlertFatigueManager       _fatigue;

    // Track deferred cluster IDs in-session (non-persisted, intent-only).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTime>
        _deferred = new();

    public UnifiedRecommendationService(
        IRecommendationArbitrator arbitrator,
        IAlertFatigueManager      fatigue)
    {
        _arbitrator = arbitrator;
        _fatigue    = fatigue;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current unified recommendation list, built from the latest
    /// arbitration output.
    ///
    /// This is a synchronous read. Call the arbitrator's arbitrate pipeline
    /// upstream to refresh the cluster set before calling here.
    /// </summary>
    public IReadOnlyList<UnifiedRecommendation> GetRecommendations(
        IReadOnlyList<RecommendationCluster> clusters) =>
        clusters
            .Select(BuildUnifiedRecommendation)
            .OrderByDescending(r => r.IsActive)
            .ThenByDescending(r => r.PriorityScore)
            .ToList()
            .AsReadOnly();

    /// <summary>
    /// Marks a recommendation cluster as deferred.
    /// The cluster will re-surface after the deferral window expires.
    /// </summary>
    public void Defer(Guid clusterId, TimeSpan deferralWindow)
    {
        _deferred[clusterId] = DateTime.UtcNow.Add(deferralWindow);
    }

    /// <summary>
    /// Clears a deferral, immediately restoring the recommendation to Active state.
    /// </summary>
    public void UndoDefer(Guid clusterId)
    {
        _deferred.TryRemove(clusterId, out _);
    }

    // ── Private ────────────────────────────────────────────────────────────────

    private UnifiedRecommendation BuildUnifiedRecommendation(RecommendationCluster cluster)
    {
        var lead        = cluster.LeadAction;
        var actionKey   = lead?.Action?.Id ?? string.Empty;

        // Fatigue transparency.
        var fatigueFactor = _fatigue.GetFatigueFactor(actionKey, AlertTolerance.Medium);
        var fatigueNote   = _fatigue.GetFatigueNote(actionKey, AlertTolerance.Medium);

        // Lifecycle state.
        var lifecycle = DetermineLifecycle(cluster, fatigueFactor);

        // Explanation: combine severity urgency phrase.
        var urgency      = SeverityNarrativeEngine.GetUrgencyPhrase(cluster.MaxSeverity);
        var severityLabel = SeverityNarrativeEngine.GetLabel(cluster.MaxSeverity);
        var colorToken    = SeverityNarrativeEngine.GetColorToken(cluster.MaxSeverity);

        // Build reasoning explanation from the inference IDs attached to the cluster.
        var reasoningExplanation = cluster.RelatedInferenceIds.Count > 0
            ? BuildReasoningExplanation(cluster)
            : string.Empty;

        return new UnifiedRecommendation
        {
            ClusterId              = cluster.ClusterId,
            Headline               = CalmCommunicationFormatter.FormatCompact(cluster.Headline),
            Summary                = CalmCommunicationFormatter.FormatStandard(
                                         lead?.Action?.Description ?? cluster.Headline),
            UrgencyPhrase          = urgency,
            SeverityLabel          = severityLabel,
            SeverityColorToken     = colorToken,
            Domain                 = cluster.Domain,
            Severity               = cluster.MaxSeverity,
            PriorityScore          = lead?.PriorityScore ?? 0,
            ActionCount            = cluster.AllActions.Count,
            LifecycleState         = lifecycle,
            FatigueNote            = fatigueNote,
            ReasoningExplanation   = reasoningExplanation,
            RelatedInferenceIds    = cluster.RelatedInferenceIds,
            SourceCluster          = cluster,
        };
    }

    private RecommendationLifecycleState DetermineLifecycle(
        RecommendationCluster cluster,
        double                fatigueFactor)
    {
        // Check for active deferral.
        if (_deferred.TryGetValue(cluster.ClusterId, out var until))
        {
            if (DateTime.UtcNow < until)
                return RecommendationLifecycleState.Deferred;
            _deferred.TryRemove(cluster.ClusterId, out _);
        }

        // Fatigue suppression: factor ≤ 0.5 means the engine has heavily deprioritized.
        if (fatigueFactor <= 0.5)
            return RecommendationLifecycleState.Expired;

        return RecommendationLifecycleState.Active;
    }

    private static string BuildReasoningExplanation(RecommendationCluster cluster)
    {
        var count = cluster.RelatedInferenceIds.Count;
        var domain = cluster.Domain.ToLowerInvariant();
        return CalmCommunicationFormatter.FormatStandard(
            $"Based on {count} active {domain} inference{(count == 1 ? "" : "s")} " +
            $"that identified {SeverityNarrativeEngine.GetPriorityWord(cluster.MaxSeverity)} " +
            $"conditions in the {cluster.Domain} area.");
    }
}
