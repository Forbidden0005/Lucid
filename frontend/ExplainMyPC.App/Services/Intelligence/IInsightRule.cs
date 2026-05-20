using ExplainMyPC.Helpers;

namespace ExplainMyPC.Services.Intelligence;

/// <summary>
/// A single deterministic heuristic that examines a telemetry snapshot and
/// its history, then decides whether a condition worth reporting is active.
///
/// Contract:
///   • Return a <see cref="SystemInsight"/> when the condition is met.
///   • Return <c>null</c> when the condition is absent or insufficient
///     data is available (cold start, sampler unavailable, etc.).
///   • <see cref="RuleId"/> must be stable across evaluations — the engine
///     uses it as a deduplication key.
///   • Implementations MUST be cheap (no I/O, no blocking). All data is
///     pre-computed and available through the two parameters.
///   • Implementations may maintain internal state (e.g., a local I/O
///     ring-buffer) when the telemetry history buffer does not track the
///     required metric.
///
/// Adding a new rule:
///   1. Create a sealed class in Services/Intelligence/Rules/.
///   2. Implement this interface.
///   3. Register an instance in SystemInsightEngine.CreateRules().
/// </summary>
public interface IInsightRule
{
    /// <summary>
    /// Stable dot-separated identifier, e.g. "cpu.sustained-high".
    /// Must be unique across all registered rules.
    /// </summary>
    string RuleId { get; }

    /// <summary>
    /// Evaluates whether the heuristic condition is currently met.
    /// </summary>
    /// <param name="current">The latest telemetry snapshot.</param>
    /// <param name="history">Rolling history for time-windowed analysis.</param>
    /// <returns>
    ///     A new <see cref="SystemInsight"/> if the condition is active,
    ///     or <c>null</c> if it is not (or if data is insufficient).
    /// </returns>
    SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history);
}
