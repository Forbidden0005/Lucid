namespace Lucid.Services.Governance;

/// <summary>
/// In-memory deferred-workload queue used by <see cref="RuntimeGovernanceService"/>
/// to track work that was refused a concurrency slot and must be retried later.
///
/// Workloads enter the queue when <see cref="ConcurrencyBudget.TryAcquire"/> returns
/// false.  The governance service drains the queue on each mode-change transition or
/// on a periodic wake-up tick, whichever comes first.
///
/// Design:
///   • FIFO within each priority class.
///   • Expired entries (older than <see cref="MaxEntryAge"/>) are silently dropped
///     on the next drain attempt so queued work never goes stale.
///   • Thread-safe via a single lock — the queue is never accessed on the hot
///     telemetry path (only from the governance thread and from callers that want
///     to enqueue deferred work).
///   • No persistence: if the app is closed, queued items are discarded.
///
/// Usage:
///   Callers enqueue a <see cref="DeferredWorkloadEntry"/> alongside a callback
///   (<see cref="RetryCallback"/>) to be invoked when the item is drained.
///   The callback is responsible for re-attempting <see cref="ConcurrencyBudget.TryAcquire"/>
///   and starting the actual work — the queue itself never starts anything.
/// </summary>
public sealed class ExecutionPriorityQueue
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>
    /// Maximum age before a queued entry is discarded. Public so callers that
    /// wait for admission (<see cref="GovernedWorkRunner.WaitForSlotAsync"/>)
    /// can bound their wait to the same lifetime.
    /// </summary>
    public static readonly TimeSpan MaxEntryAge = TimeSpan.FromMinutes(30);

    // ── Internal record ───────────────────────────────────────────────────────

    private sealed record QueuedEntry(
        DeferredWorkloadEntry Metadata,
        Func<Task>            RetryCallback);

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly object             _lock    = new();
    private readonly List<QueuedEntry>  _entries = [];

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Enqueues a deferred workload for later retry.
    /// </summary>
    /// <param name="entry">Metadata describing the deferred work (used for display).</param>
    /// <param name="retryCallback">
    ///   The callback invoked when the queue is drained. Must be idempotent — it is
    ///   called at most once per enqueue. Responsible for re-checking budget and
    ///   starting work if the slot is now available.
    /// </param>
    public void Enqueue(DeferredWorkloadEntry entry, Func<Task> retryCallback)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(retryCallback);

        lock (_lock)
            _entries.Add(new QueuedEntry(entry, retryCallback));
    }

    /// <summary>
    /// Drains all non-expired entries, invoking each retry callback in
    /// priority order (Foreground first, then Background, then IdleOnly).
    ///
    /// Expired entries are silently discarded.
    /// Each callback is fire-and-forget; exceptions are swallowed so one
    /// failed retry never interrupts the others.
    /// </summary>
    public void Drain()
    {
        List<QueuedEntry> toRetry;

        lock (_lock)
        {
            var now = DateTimeOffset.Now;

            // Remove expired entries first
            _entries.RemoveAll(e =>
                (now - e.Metadata.DeferredAt) > MaxEntryAge);

            if (_entries.Count == 0)
                return;

            // Take a prioritised snapshot and clear
            toRetry = [.. _entries
                .OrderBy(e => e.Metadata.Priority)
                .ThenBy(e => e.Metadata.DeferredAt)];
            _entries.Clear();
        }

        foreach (var entry in toRetry)
        {
            try   { _ = entry.RetryCallback(); }
            catch { /* best-effort — individual retry failures are not fatal */ }
        }
    }

    /// <summary>
    /// Returns a read-only snapshot of all currently queued entries, ordered
    /// by priority then by deferral time. Used by the governance UI page.
    /// </summary>
    public IReadOnlyList<DeferredWorkloadEntry> GetSnapshot()
    {
        lock (_lock)
            return [.. _entries
                .OrderBy(e => e.Metadata.Priority)
                .ThenBy(e => e.Metadata.DeferredAt)
                .Select(e => e.Metadata)];
    }

    /// <summary>Number of entries currently waiting in the queue.</summary>
    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    /// <summary>Discards all queued entries. Called on shutdown.</summary>
    public void Clear()
    {
        lock (_lock) _entries.Clear();
    }
}
