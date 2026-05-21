namespace ExplainMyPC.Services.Governance;

/// <summary>
/// Public contract of the runtime governance service.
///
/// The governance service is the single authority on what the runtime is doing,
/// how loaded it is, and whether a given workload is allowed to start. All
/// background services and heavy executors should consult this service before
/// starting expensive work.
///
/// Lifecycle: call <see cref="Start"/> once and <see cref="Stop"/> on shutdown.
/// The service begins monitoring after <see cref="Start"/> returns.
/// </summary>
public interface IRuntimeGovernanceService
{
    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the UI thread whenever the runtime mode changes.
    /// Subscribers should use this to update UI state or start/stop work.
    /// </summary>
    event EventHandler<RuntimeModeChangedEventArgs>? ModeChanged;

    // ── Current state ─────────────────────────────────────────────────────────

    /// <summary>The most recently evaluated runtime mode.</summary>
    RuntimeMode CurrentMode { get; }

    /// <summary>The active throttling reasons (may be None).</summary>
    ThrottlingReason CurrentReasons { get; }

    /// <summary>
    /// Returns a point-in-time snapshot of the full resource budget.
    /// Useful for the governance page and for diagnostic logging.
    /// </summary>
    ResourceBudgetSnapshot GetSnapshot();

    /// <summary>Returns all workloads currently holding concurrency slots.</summary>
    IReadOnlyList<ActiveWorkloadEntry> GetActiveWorkloads();

    /// <summary>Returns all workloads currently waiting in the deferred queue.</summary>
    IReadOnlyList<DeferredWorkloadEntry> GetDeferredWorkloads();

    // ── Workload coordination ─────────────────────────────────────────────────

    /// <summary>
    /// Attempts to acquire a concurrency slot for the given workload.
    ///
    /// Returns <c>true</c> and sets <paramref name="refusalReason"/> to
    /// <c>null</c> when the slot is granted.
    ///
    /// Returns <c>false</c> and sets <paramref name="refusalReason"/> to a
    /// human-readable explanation when the slot cannot be granted due to:
    ///   • Per-category hard slot limit already reached.
    ///   • Background ceiling reached for the current mode.
    ///   • IdleOnly work attempted while mode is non-Normal.
    /// </summary>
    bool TryAcquireSlot(
        WorkloadCategory category,
        string           workloadName,
        out string?      refusalReason);

    /// <summary>
    /// Releases a previously acquired slot. Safe to call even if the slot
    /// was never successfully acquired (no-op).
    /// </summary>
    void ReleaseSlot(WorkloadCategory category, string workloadName);

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>Starts the governance monitor loop.</summary>
    void Start();

    /// <summary>Stops the governance monitor loop.</summary>
    void Stop();
}

/// <summary>
/// Event args for <see cref="IRuntimeGovernanceService.ModeChanged"/>.
/// </summary>
public sealed class RuntimeModeChangedEventArgs(
    RuntimeMode      previousMode,
    RuntimeMode      newMode,
    ThrottlingReason reasons) : EventArgs
{
    public RuntimeMode      PreviousMode { get; } = previousMode;
    public RuntimeMode      NewMode      { get; } = newMode;
    public ThrottlingReason Reasons      { get; } = reasons;

    /// <summary>Human-readable description of what triggered this transition.</summary>
    public string Description => RuntimePressureAnalyzer.DescribeReasons(Reasons);
}
