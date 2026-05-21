using ExplainMyPC.Services.Governance;
using ExplainMyPC.Services.Persistence;

namespace ExplainMyPC.Services.Diagnostics;

/// <summary>
/// Executes concrete recovery actions available to the diagnostics layer.
///
/// Recovery actions are limited to operations that are:
///   • Safe: cannot cause data loss or leave the app in a worse state.
///   • Reversible or idempotent: can be applied multiple times safely.
///   • Bounded: complete in a predictable, short time window.
///
/// Available actions:
///   flush-persistence   — Drain the SQLite write queue immediately.
///   clear-stuck-slots   — Release concurrency slots that have been held too long.
///   reset-polling-rate  — Push the polling coordinator back to Normal-mode rates.
///   clear-deferred-queue — Clear the execution priority queue.
///
/// Dangerous operations (e.g. restarting services, deleting state) are NOT
/// included — those require explicit user action.
///
/// Thread safety: all public methods are safe to call from any thread.
/// </summary>
public sealed class RecoveryCoordinator
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>Workload slots active longer than this threshold are candidates for release.</summary>
    private static readonly TimeSpan StuckSlotThreshold = TimeSpan.FromMinutes(10);

    // ── Available actions ─────────────────────────────────────────────────────

    public static readonly IReadOnlyList<RecoveryAction> AvailableActions =
    [
        new RecoveryAction(
            ActionId:     "flush-persistence",
            Label:        "Flush Persistence Queue",
            Description:  "Immediately drain all pending SQLite write operations. " +
                          "Useful when the persistence queue is building up.",
            IsDestructive: false),

        new RecoveryAction(
            ActionId:     "clear-stuck-slots",
            Label:        "Clear Stuck Workloads",
            Description:  "Release concurrency slots held longer than 10 minutes. " +
                          "Frees capacity if a workload silently stopped without releasing its slot.",
            IsDestructive: false),

        new RecoveryAction(
            ActionId:     "reset-polling-rate",
            Label:        "Reset Polling Rate",
            Description:  "Restore telemetry polling to Normal-mode rates. " +
                          "Use if the platform is stuck at a reduced polling rate.",
            IsDestructive: false),

        new RecoveryAction(
            ActionId:     "clear-deferred-queue",
            Label:        "Clear Deferred Queue",
            Description:  "Discard all workloads waiting in the execution priority queue. " +
                          "Use if the deferred queue has accumulated stale entries.",
            IsDestructive: false),
    ];

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly SQLitePersistenceService? _persistence;
    private readonly ConcurrencyBudget?        _budget;
    private readonly PollingCoordinator?       _polling;
    private readonly ExecutionPriorityQueue?   _queue;
    private readonly DiagnosticsEventAggregator _events;

    // ── Construction ──────────────────────────────────────────────────────────

    public RecoveryCoordinator(
        DiagnosticsEventAggregator events,
        SQLitePersistenceService?  persistence  = null,
        ConcurrencyBudget?         budget       = null,
        PollingCoordinator?        polling      = null,
        ExecutionPriorityQueue?    queue        = null)
    {
        _events      = events;
        _persistence = persistence;
        _budget      = budget;
        _polling     = polling;
        _queue       = queue;
    }

    // ── Execution ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the recovery action identified by <paramref name="actionId"/>.
    /// Always returns a result — never throws.
    /// </summary>
    public async Task<RecoveryResult> ExecuteAsync(string actionId)
    {
        _events.Append(
            DiagnosticsEventType.RecoveryAttempt,
            DiagnosticSeverity.Info,
            "RecoveryCoordinator",
            $"Recovery action started: {actionId}");

        try
        {
            var result = actionId switch
            {
                "flush-persistence"    => await FlushPersistenceAsync(),
                "clear-stuck-slots"    => ClearStuckSlots(),
                "reset-polling-rate"   => ResetPollingRate(),
                "clear-deferred-queue" => ClearDeferredQueue(),
                _                      => new RecoveryResult(actionId, false,
                                             $"Unknown recovery action: {actionId}"),
            };

            _events.Append(
                result.Succeeded
                    ? DiagnosticsEventType.RecoverySucceeded
                    : DiagnosticsEventType.RecoveryFailed,
                result.Succeeded ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning,
                "RecoveryCoordinator",
                result.Message);

            return result;
        }
        catch (Exception ex)
        {
            _events.Append(
                DiagnosticsEventType.RecoveryFailed,
                DiagnosticSeverity.Warning,
                "RecoveryCoordinator",
                $"Recovery action '{actionId}' threw unexpectedly.",
                ex.Message);

            return new RecoveryResult(actionId, false,
                "Recovery action failed with an unexpected error.", ex.Message);
        }
    }

    // ── Individual actions ────────────────────────────────────────────────────

    private async Task<RecoveryResult> FlushPersistenceAsync()
    {
        if (_persistence is null)
            return new RecoveryResult("flush-persistence", false,
                "Persistence service is not available.");

        if (!_persistence.IsHealthy)
            return new RecoveryResult("flush-persistence", false,
                "Persistence database is not healthy — cannot flush.");

        await _persistence.FlushQueueAsync().ConfigureAwait(false);

        int remaining = _persistence.PendingWriteCount;
        return new RecoveryResult("flush-persistence", true,
            $"Persistence queue flushed. Remaining queued writes: {remaining}.");
    }

    private RecoveryResult ClearStuckSlots()
    {
        if (_budget is null)
            return new RecoveryResult("clear-stuck-slots", false,
                "Concurrency budget is not available.");

        var active  = _budget.GetActiveWorkloads();
        var now     = DateTimeOffset.Now;
        int cleared = 0;

        foreach (var w in active)
        {
            if ((now - w.StartedAt) > StuckSlotThreshold)
            {
                try
                {
                    _budget.Release(w.Category, w.Name);
                    cleared++;
                }
                catch { /* best-effort */ }
            }
        }

        return new RecoveryResult("clear-stuck-slots", true,
            cleared == 0
                ? "No stuck workload slots found."
                : $"Released {cleared} workload slot(s) that had been active " +
                  $"for more than {StuckSlotThreshold.TotalMinutes:F0} minutes.");
    }

    private RecoveryResult ResetPollingRate()
    {
        if (_polling is null)
            return new RecoveryResult("reset-polling-rate", false,
                "Polling coordinator is not available.");

        _polling.ApplyMode(ExplainMyPC.Services.Governance.RuntimeMode.Normal);

        return new RecoveryResult("reset-polling-rate", true,
            "Polling rates reset to Normal-mode defaults " +
            "(telemetry: 1.5 s, process: 4.5 s).");
    }

    private RecoveryResult ClearDeferredQueue()
    {
        if (_queue is null)
            return new RecoveryResult("clear-deferred-queue", false,
                "Execution priority queue is not available.");

        int before = _queue.Count;
        _queue.Clear();

        return new RecoveryResult("clear-deferred-queue", true,
            before == 0
                ? "Deferred queue was already empty."
                : $"Cleared {before} deferred workload(s) from the execution queue.");
    }
}
