using System.Collections.Concurrent;

namespace Lucid.Services.Reasoning.Memory;

/// <summary>
/// Tracks the outcome of each recommendation surfaced by Lucid.
///
/// An outcome represents what the user or system did with a recommendation:
///   - Accepted (user ran the action)
///   - Rejected (user explicitly declined)
///   - Dismissed (user dismissed without deciding)
///   - Expired (recommendation left the surface without user interaction)
///
/// Outcomes are used by <see cref="Lucid.Services.Learning.IRemediationLearningService"/>
/// to personalize future recommendation ranking and by
/// <see cref="PatternConsistencyAnalyzer"/> to weight historical inference strength.
///
/// Thread-safe: all methods use concurrent collections.
/// </summary>
public sealed class RecommendationOutcomeTracker
{
    private const int MaxOutcomesPerAction = 50;

    private readonly ConcurrentDictionary<string, ConcurrentQueue<RecommendationOutcome>> _outcomes
        = new(StringComparer.OrdinalIgnoreCase);

    // ── Recording ─────────────────────────────────────────────────────────────

    /// <summary>Records that the user executed or accepted a recommendation.</summary>
    public void RecordAccepted(string actionId, string insightId, string? context = null) =>
        Append(actionId, new RecommendationOutcome(
            ActionId:   actionId,
            InsightId:  insightId,
            Outcome:    OutcomeType.Accepted,
            OccurredAt: DateTime.UtcNow,
            Context:    context));

    /// <summary>Records that the user explicitly declined a recommendation.</summary>
    public void RecordRejected(string actionId, string insightId, string? reason = null) =>
        Append(actionId, new RecommendationOutcome(
            ActionId:   actionId,
            InsightId:  insightId,
            Outcome:    OutcomeType.Rejected,
            OccurredAt: DateTime.UtcNow,
            Context:    reason));

    /// <summary>Records that the recommendation was dismissed (no explicit decision).</summary>
    public void RecordDismissed(string actionId) =>
        Append(actionId, new RecommendationOutcome(
            ActionId:   actionId,
            InsightId:  string.Empty,
            Outcome:    OutcomeType.Dismissed,
            OccurredAt: DateTime.UtcNow));

    /// <summary>Records that a recommendation expired without user interaction.</summary>
    public void RecordExpired(string actionId) =>
        Append(actionId, new RecommendationOutcome(
            ActionId:   actionId,
            InsightId:  string.Empty,
            Outcome:    OutcomeType.Expired,
            OccurredAt: DateTime.UtcNow));

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>Returns all recorded outcomes for the given action, most recent first.</summary>
    public IReadOnlyList<RecommendationOutcome> GetOutcomes(string actionId)
    {
        if (!_outcomes.TryGetValue(actionId, out var queue)) return [];
        return [.. queue.Reverse()];
    }

    /// <summary>
    /// Returns the acceptance rate for the given action (0.0–1.0).
    /// Returns 0.5 (neutral) when there are no outcomes yet.
    /// </summary>
    public double GetAcceptanceRate(string actionId)
    {
        if (!_outcomes.TryGetValue(actionId, out var queue)) return 0.5;
        var list = queue.ToArray();
        if (list.Length == 0) return 0.5;
        var accepted = list.Count(o => o.Outcome == OutcomeType.Accepted);
        return (double)accepted / list.Length;
    }

    /// <summary>Total number of actions with at least one tracked outcome.</summary>
    public int TrackedActionCount => _outcomes.Count;

    // ── Private ───────────────────────────────────────────────────────────────

    private void Append(string actionId, RecommendationOutcome outcome)
    {
        var queue = _outcomes.GetOrAdd(actionId, _ => new ConcurrentQueue<RecommendationOutcome>());
        queue.Enqueue(outcome);

        // Bound memory per action
        while (queue.Count > MaxOutcomesPerAction)
            queue.TryDequeue(out _);
    }
}

/// <summary>
/// A single outcome record for a recommendation.
/// </summary>
public sealed record RecommendationOutcome(
    string     ActionId,
    string     InsightId,
    OutcomeType Outcome,
    DateTime   OccurredAt,
    string?    Context = null);

/// <summary>
/// The disposition of a surfaced recommendation.
/// </summary>
public enum OutcomeType
{
    /// <summary>User ran or accepted the recommended action.</summary>
    Accepted  = 0,

    /// <summary>User explicitly declined the recommendation.</summary>
    Rejected  = 1,

    /// <summary>Recommendation was dismissed by the user without a decision.</summary>
    Dismissed = 2,

    /// <summary>Recommendation left the surface without any user interaction.</summary>
    Expired   = 3,
}
