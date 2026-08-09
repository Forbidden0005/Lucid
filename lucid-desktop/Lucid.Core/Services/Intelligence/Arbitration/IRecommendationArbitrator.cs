using Lucid.Services.Intelligence;
using Lucid.Services.Reasoning.Context;
using Lucid.Services.Reasoning.Cognitive;

namespace Lucid.Services.Intelligence.Arbitration;

/// <summary>
/// Centralizes recommendation prioritization, deduplication, clustering,
/// and context-aware suppression.
///
/// The arbitrator sits between the intelligence engine's raw insight/action
/// output and the UI recommendation display. It answers:
///   - Which of these actions are worth showing right now?
///   - Which are duplicates I've already shown recently?
///   - Which are contextually inappropriate for this workload?
///   - How should related actions be grouped for clarity?
///
/// Composites:
///   - <see cref="IGlobalRecommendationPrioritizer"/> — prioritization scoring
///   - <see cref="IAlertFatigueManager"/> — rate limiting and fatigue suppression
///   - <see cref="ContextSuppressionEngine"/> — context-based suppression
///
/// Thread-safe: all methods are safe for concurrent callers.
/// </summary>
public interface IRecommendationArbitrator
{
    /// <summary>
    /// Arbitrates a set of raw insights and their candidate actions.
    ///
    /// Returns a deduplicated, prioritized, context-filtered list of
    /// <see cref="RecommendationCluster"/> objects ready for display.
    ///
    /// The result is ordered by cluster severity descending — critical clusters first.
    /// </summary>
    /// <param name="insights">Active insights from the intelligence engine.</param>
    /// <param name="context">
    /// Current operational context for suppression decisions.
    /// Pass null when context is not yet established.
    /// </param>
    /// <param name="activeInferences">
    /// Current cognitive inferences — used to link clusters to reasoning evidence.
    /// Pass empty list when not available.
    /// </param>
    IReadOnlyList<RecommendationCluster> Arbitrate(
        IReadOnlyList<SystemInsight>     insights,
        ContextProfile?                  context,
        IReadOnlyList<OperationalInference> activeInferences);

    /// <summary>
    /// Returns the count of recommendations that were suppressed in the last
    /// arbitration cycle, with suppression reasons.
    /// Exposed for the Diagnostics page.
    /// </summary>
    IReadOnlyList<(string ActionId, string Reason)> GetLastCycleSuppressed();
}
