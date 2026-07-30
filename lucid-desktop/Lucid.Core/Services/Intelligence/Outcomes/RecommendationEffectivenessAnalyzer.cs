using Lucid.Services.Learning;
using Lucid.Services.Reasoning.Memory;

namespace Lucid.Services.Intelligence.Outcomes;

/// <summary>
/// Analyzes recommendation effectiveness by combining Phase 19 outcome tracking
/// with the existing <see cref="IRemediationLearningService"/> profiles.
///
/// This analyzer bridges two outcome data sources:
///   1. <see cref="RecommendationOutcomeTracker"/> (Phase 19) — raw accept/reject/dismiss
///   2. <see cref="IRemediationLearningService"/> — stabilization-confirmed effectiveness
///
/// The result is an <see cref="OutcomeEffectivenessRecord"/> that incorporates both
/// signals to determine whether a recommendation type is genuinely useful.
///
/// Key invariant: effectiveness assessment is NEVER used to optimize engagement.
/// It is ONLY used to reduce noise (suppress low-value recommendations)
/// and strengthen high-value ones.
///
/// Thread-safe: all inputs are read-only; no mutable state.
/// </summary>
public sealed class RecommendationEffectivenessAnalyzer
{
    private readonly RecommendationOutcomeTracker  _outcomeTracker;
    private readonly IRemediationLearningService   _learningService;

    public RecommendationEffectivenessAnalyzer(
        RecommendationOutcomeTracker outcomeTracker,
        IRemediationLearningService  learningService)
    {
        _outcomeTracker  = outcomeTracker;
        _learningService = learningService;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an <see cref="OutcomeEffectivenessRecord"/> for the given action ID
    /// by combining all available outcome signals.
    /// </summary>
    public OutcomeEffectivenessRecord Analyze(string actionId)
    {
        var rawOutcomes = _outcomeTracker.GetOutcomes(actionId);

        int accepted  = rawOutcomes.Count(o => o.Outcome == OutcomeType.Accepted);
        int rejected  = rawOutcomes.Count(o => o.Outcome == OutcomeType.Rejected);
        int dismissed = rawOutcomes.Count(o => o.Outcome == OutcomeType.Dismissed);
        int expired   = rawOutcomes.Count(o => o.Outcome == OutcomeType.Expired);

        // Check stabilization-confirmed effectiveness from the learning service.
        bool? conditionImproved = null;
        var profile = _learningService.GetProfile(actionId);
        if (profile is not null && profile.TotalAnalyzed >= 3)
        {
            // EffectivenessRate > 0.60 indicates the action genuinely helped.
            conditionImproved = profile.EffectivenessRate > 0.60;
        }

        return new OutcomeEffectivenessRecord
        {
            ActionId          = actionId,
            AcceptanceCount   = accepted,
            DismissalCount    = rejected + dismissed,
            ExpiryCount       = expired,
            ConditionImproved = conditionImproved,
        };
    }

    /// <summary>
    /// Returns effectiveness records for all action IDs that have been tracked,
    /// sorted by effectiveness score descending.
    /// </summary>
    public IReadOnlyList<OutcomeEffectivenessRecord> AnalyzeAll()
    {
        // Get all known action IDs from both sources.
        var actionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in _learningService.GetAllProfiles().Keys)
            actionIds.Add(profile);

        return actionIds
            .Select(Analyze)
            .OrderByDescending(r => r.EffectivenessScore)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Returns true when the given action appears low-value and should be deprioritized.
    /// Only returns true when there is sufficient data to make a reliable assessment.
    /// </summary>
    public bool IsLowValue(string actionId) => Analyze(actionId).IsLowValue;
}
