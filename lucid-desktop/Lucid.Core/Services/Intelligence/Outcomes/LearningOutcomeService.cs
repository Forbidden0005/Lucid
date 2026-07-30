using Lucid.Services.Learning;
using Lucid.Services.Reasoning.Memory;

namespace Lucid.Services.Intelligence.Outcomes;

/// <summary>
/// Coordinates recommendation outcome learning from all available data sources.
///
/// Consolidates:
///   1. <see cref="RecommendationOutcomeTracker"/> (Phase 19) — raw event stream
///   2. <see cref="IRemediationLearningService"/>  — execution + stabilization profiles
///   3. <see cref="IInterventionMemoryService"/>   — accept/dismiss interaction history
///
/// Provides a unified view of recommendation usefulness to:
///   - Confidence calibration systems
///   - Attention coordinator (suppress low-value items)
///   - Explainability layer (explain why items were deprioritized)
///   - Learning diagnostics (what Lucid has learned)
///
/// Ethics constraint: outcome data is NEVER used to optimize engagement.
/// It is only used to reduce false positives and improve operational accuracy.
///
/// Thread-safe: delegates to thread-safe services, no mutable state.
/// </summary>
public sealed class LearningOutcomeService
{
    private readonly RecommendationEffectivenessAnalyzer _analyzer;
    private readonly IInterventionMemoryService          _interventionMemory;
    private readonly IRemediationLearningService         _learningService;

    public LearningOutcomeService(
        RecommendationEffectivenessAnalyzer analyzer,
        IInterventionMemoryService          interventionMemory,
        IRemediationLearningService         learningService)
    {
        _analyzer           = analyzer;
        _interventionMemory = interventionMemory;
        _learningService    = learningService;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the effectiveness score [0, 1] for an action ID.
    /// Returns 0.5 (neutral) when there is insufficient data.
    /// </summary>
    public double GetEffectivenessScore(string actionId)
    {
        var record = _analyzer.Analyze(actionId);
        return record.EffectivenessScore;
    }

    /// <summary>
    /// Returns true when an action ID has been identified as low-value
    /// based on acceptance rate and condition improvement history.
    /// Only returns true with sufficient supporting data.
    /// </summary>
    public bool IsLowValue(string actionId) => _analyzer.IsLowValue(actionId);

    /// <summary>
    /// Returns the recent interaction count for an action ID (shows in last N days).
    /// Used to compute alert fatigue.
    /// </summary>
    public int GetRecentShowCount(string actionId, int withinDays = 7) =>
        _interventionMemory.GetRecentShowCount(actionId, withinDays);

    /// <summary>
    /// Returns effectiveness records for all tracked actions,
    /// sorted by effectiveness score descending.
    /// </summary>
    public IReadOnlyList<OutcomeEffectivenessRecord> GetAllEffectiveness() =>
        _analyzer.AnalyzeAll();

    /// <summary>
    /// Returns a brief explanation of why a recommendation's effectiveness is what it is.
    /// Used by the explainability layer.
    /// </summary>
    public string ExplainEffectiveness(string actionId)
    {
        var record  = _analyzer.Analyze(actionId);
        var profile = _learningService.GetProfile(actionId);

        if (!record.HasSufficientData)
            return "Insufficient interaction data to assess effectiveness.";

        var parts = new List<string>(3);

        parts.Add($"Accepted {record.AcceptanceCount}× / " +
                  $"dismissed {record.DismissalCount}× " +
                  $"(acceptance rate: {record.AcceptanceRate:P0}).");

        if (profile is not null && profile.TotalAnalyzed >= 2)
            parts.Add($"Executed {profile.TotalAnalyzed} time(s); " +
                      $"effectiveness rate: {profile.EffectivenessRate:P0}.");

        if (record.ConditionImproved == true)
            parts.Add("Underlying condition improved after action was taken.");
        else if (record.ConditionImproved == false)
            parts.Add("Underlying condition did not improve measurably after action.");

        return string.Join(" ", parts);
    }
}
