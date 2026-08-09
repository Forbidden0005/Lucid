namespace Lucid.Services.Governance;

/// <summary>
/// Shared entry points for running work under runtime governance with
/// defer-for-retry semantics.
///
/// Historically, call sites that were refused a slot by
/// <see cref="IRuntimeGovernanceService.TryAcquireSlot"/> simply dropped the
/// work (governance adoption audit 2026-07-25, §1 "Dead / unwired parts").
/// This helper closes that gap: refused work is registered on the
/// <see cref="ExecutionPriorityQueue"/> via
/// <see cref="IRuntimeGovernanceService.DeferWorkload"/> and retried when the
/// queue drains (a slot is released or the runtime mode changes).
///
/// Two shapes are provided:
///   • <see cref="RunOrDeferAsync"/> — fire-and-forget style for periodic /
///     background cycles (timers, scheduled passes). The work delegate is
///     re-invoked from the queue drain when a slot frees up.
///   • <see cref="WaitForSlotAsync"/> — awaitable admission for user-initiated
///     operations. The caller keeps awaiting (so its UI event subscriptions
///     stay alive) and resumes when a slot is granted, the wait expires
///     (<see cref="ExecutionPriorityQueue.MaxEntryAge"/>), or the caller cancels.
///
/// Re-deferral note: each failed retry re-enqueues with a fresh timestamp, so
/// under sustained pressure deferred work stays pending until pressure clears
/// rather than expiring mid-storm. The queue's 30-minute expiry only drops
/// entries that no drain ever picked up.
/// </summary>
public static class GovernedWorkRunner
{
    /// <summary>
    /// Runs <paramref name="work"/> now if a governance slot is available,
    /// otherwise defers it for retry on the next queue drain.
    ///
    /// Returns <c>true</c> when the work ran (or faulted) now; <c>false</c>
    /// when it was deferred (or was already waiting in the deferred queue).
    /// The slot is always released when the work completes.
    /// </summary>
    /// <param name="onDeferred">
    /// Invoked with the human-readable refusal reason each time the work is
    /// deferred instead of run. Optional.
    /// </param>
    public static async Task<bool> RunOrDeferAsync(
        IRuntimeGovernanceService governance,
        WorkloadCategory          category,
        string                    workloadName,
        Func<Task>                work,
        Action<string>?           onDeferred = null)
    {
        ArgumentNullException.ThrowIfNull(governance);
        ArgumentNullException.ThrowIfNull(work);

        if (!governance.TryAcquireSlot(category, workloadName, out string? refusal))
        {
            string reason = refusal ?? "System is busy.";

            // Periodic timers can fire again while a previous cycle is still
            // waiting for a slot — never stack duplicate entries for the same
            // workload.
            bool alreadyQueued = governance.GetDeferredWorkloads()
                .Any(e => e.Category == category && e.Name == workloadName);

            if (!alreadyQueued)
            {
                governance.DeferWorkload(category, workloadName, reason, async () =>
                {
                    // Retry runs fire-and-forget from the queue drain: a fault
                    // here must never surface as an unobserved task exception.
                    try
                    {
                        await RunOrDeferAsync(governance, category, workloadName, work, onDeferred)
                            .ConfigureAwait(false);
                    }
                    catch { /* best-effort retry — work delegates own their error handling */ }
                });
            }

            onDeferred?.Invoke(reason);
            return false;
        }

        try
        {
            await work().ConfigureAwait(false);
        }
        finally
        {
            governance.ReleaseSlot(category, workloadName);
        }

        return true;
    }

    /// <summary>
    /// Acquires a governance slot, waiting (via the deferred queue) when the
    /// budget refuses admission. Returns <c>true</c> with the slot HELD — the
    /// caller must call <see cref="IRuntimeGovernanceService.ReleaseSlot"/>
    /// when its work completes. Returns <c>false</c> when the wait expired
    /// without a slot becoming available.
    /// </summary>
    /// <exception cref="OperationCanceledException">
    /// The caller cancelled while waiting. No slot is held.
    /// </exception>
    public static async Task<bool> WaitForSlotAsync(
        IRuntimeGovernanceService governance,
        WorkloadCategory          category,
        string                    workloadName,
        CancellationToken         ct         = default,
        Action<string>?           onDeferred = null)
    {
        ArgumentNullException.ThrowIfNull(governance);

        if (governance.TryAcquireSlot(category, workloadName, out string? refusal))
            return true;

        onDeferred?.Invoke(refusal ?? "System is busy.");

        var deadline = DateTimeOffset.Now + ExecutionPriorityQueue.MaxEntryAge;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var remaining = deadline - DateTimeOffset.Now;
            if (remaining <= TimeSpan.Zero)
                return false;

            // The queue drain wakes this waiter; the waiter itself re-attempts
            // the acquire (per the ExecutionPriorityQueue callback contract).
            var wake = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            governance.DeferWorkload(category, workloadName,
                refusal ?? "System is busy.",
                () => { wake.TrySetResult(); return Task.CompletedTask; });

            try
            {
                await wake.Task.WaitAsync(remaining, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return false;
            }

            if (governance.TryAcquireSlot(category, workloadName, out refusal))
                return true;
        }
    }
}
