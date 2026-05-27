namespace Lucid.Services.Intelligence;

/// <summary>
/// A cross-insight correlation heuristic evaluated after all base
/// <see cref="IInsightRule"/> results are collected.
///
/// Synthesis rules receive the full list of active base findings and look for
/// patterns that span multiple insights — most commonly, the same process
/// appearing as the attributed cause across two or more distinct findings.
///
/// Contract (mirrors IInsightRule):
///   • Return zero or more <see cref="SystemInsight"/> objects when cross-insight
///     conditions are detected; return an empty list otherwise.
///   • IDs of returned insights MUST start with <c>"synthesis."</c> so the engine
///     can distinguish them from base findings and avoid namespace collisions.
///   • IDs MUST be deterministic for a given input so repeated evaluations of the
///     same state produce the same ID — the engine relies on this for change detection.
///   • Implementations MUST be cheap: no I/O, no blocking, pure in-memory reasoning.
///   • Exceptions are swallowed by <see cref="InsightSynthesisEngine"/>; a crashing
///     rule never disrupts base findings or other synthesis rules.
///
/// Adding a new synthesis rule:
///   1. Create a sealed class in Services/Intelligence/Rules/.
///   2. Implement this interface.
///   3. Register an instance in InsightSynthesisEngine (via SystemInsightEngine).
/// </summary>
public interface ISynthesisRule
{
    /// <summary>
    /// Stable dot-separated identifier for this synthesis rule, e.g.
    /// "synthesis.shared-process". Used for logging and fault isolation only —
    /// not used as the ID of produced insights (each produced insight has its own
    /// deterministic ID derived from the contributing findings and process names).
    /// </summary>
    string RuleId { get; }

    /// <summary>
    /// Evaluates cross-insight correlation over the current active base findings.
    /// </summary>
    /// <param name="baseInsights">
    ///     Active findings from all base <see cref="IInsightRule"/> evaluations
    ///     this tick. Never contains previously synthesized insights.
    /// </param>
    /// <returns>
    ///     Zero or more synthesized insights. All IDs must start with
    ///     <c>"synthesis."</c>. Return an empty list when no correlation is found.
    /// </returns>
    IReadOnlyList<SystemInsight> Evaluate(IReadOnlyList<SystemInsight> baseInsights);
}
