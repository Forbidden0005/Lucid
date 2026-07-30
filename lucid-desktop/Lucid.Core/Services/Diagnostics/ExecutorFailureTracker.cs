namespace Lucid.Services.Diagnostics;

/// <summary>
/// Tracks per-executor failure history for all action executors.
///
/// Called by <see cref="GovernanceAwareExecutionEngine"/> after each execution.
/// Maintains cumulative counters and the last failure detail string so the
/// diagnostics page can surface repeated executor failures.
///
/// Design:
///   • Mutable internal state guarded by a single lock — lightweight.
///   • Stores only the last failure detail (not all failures) to bound memory use.
///   • A "concerning" executor is one with ≥3 consecutive failures or ≥20% failure rate.
///
/// Thread safety: all public methods are safe to call from any thread.
/// </summary>
public sealed class ExecutorFailureTracker
{
    // ── Configuration ─────────────────────────────────────────────────────────

    private const int    ConsecutiveFailureThreshold = 3;
    private const double FailureRateThreshold        = 0.20;

    // ── Internal mutable state ────────────────────────────────────────────────

    private sealed class MutableEntry
    {
        public int             TotalExecutions      { get; set; }
        public int             TotalFailures        { get; set; }
        public int             ConsecutiveFailures  { get; set; }
        public DateTimeOffset? LastFailureAt        { get; set; }
        public string?         LastFailureDetail    { get; set; }

        public ExecutorFailureSummary ToRecord(string actionId) => new(
            ActionId:            actionId,
            TotalExecutions:     TotalExecutions,
            TotalFailures:       TotalFailures,
            ConsecutiveFailures: ConsecutiveFailures,
            LastFailureAt:       LastFailureAt,
            LastFailureDetail:   LastFailureDetail);
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly object _lock = new();
    private readonly Dictionary<string, MutableEntry> _entries = new();

    // ── Recording ─────────────────────────────────────────────────────────────

    /// <summary>Records a successful executor invocation.</summary>
    public void RecordSuccess(string actionId)
    {
        lock (_lock)
        {
            var e = GetOrCreate(actionId);
            e.TotalExecutions++;
            e.ConsecutiveFailures = 0;
        }
    }

    /// <summary>
    /// Records an executor failure.
    /// </summary>
    /// <param name="actionId">Stable action ID (e.g. "action.repair.sfc-scannow").</param>
    /// <param name="errorDetail">
    ///   Human-readable error text. Truncated to 300 characters to bound memory.
    /// </param>
    public void RecordFailure(string actionId, string? errorDetail)
    {
        lock (_lock)
        {
            var e = GetOrCreate(actionId);
            e.TotalExecutions++;
            e.TotalFailures++;
            e.ConsecutiveFailures++;
            e.LastFailureAt     = DateTimeOffset.Now;
            e.LastFailureDetail = errorDetail is { Length: > 300 }
                ? string.Concat(errorDetail.AsSpan(0, 297), "…")
                : errorDetail;
        }
    }

    // ── Observation ───────────────────────────────────────────────────────────

    /// <summary>Returns a failure summary for the named executor, or null if never seen.</summary>
    public ExecutorFailureSummary? GetSummary(string actionId)
    {
        lock (_lock)
            return _entries.TryGetValue(actionId, out var e) ? e.ToRecord(actionId) : null;
    }

    /// <summary>Returns all executor summaries ordered by failure count descending.</summary>
    public IReadOnlyList<ExecutorFailureSummary> GetAll()
    {
        lock (_lock)
            return [.. _entries
                .Select(kvp => kvp.Value.ToRecord(kvp.Key))
                .OrderByDescending(s => s.TotalFailures)];
    }

    /// <summary>
    /// Returns summaries for executors that look concerning (high failure rate or
    /// repeated consecutive failures). Used for the diagnostics highlight panel.
    /// </summary>
    public IReadOnlyList<ExecutorFailureSummary> GetConcerning()
    {
        lock (_lock)
            return [.. _entries
                .Select(kvp => kvp.Value.ToRecord(kvp.Key))
                .Where(s => s.ConsecutiveFailures >= ConsecutiveFailureThreshold
                         || s.FailureRate >= FailureRateThreshold)
                .OrderByDescending(s => s.ConsecutiveFailures)];
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private MutableEntry GetOrCreate(string actionId)
    {
        if (!_entries.TryGetValue(actionId, out var e))
        {
            e = new MutableEntry();
            _entries[actionId] = e;
        }
        return e;
    }
}
