namespace Lucid.Services.Automation;

/// <summary>
/// Provides stop/cancel/emergency-halt hooks that the user can trigger at any
/// point during automation execution.
///
/// This is the single authority for interruption. Every executing step checks
/// <see cref="IsInterrupted"/> before proceeding to the next action.
///
/// Safety guarantee: if the user calls <see cref="EmergencyHalt"/>, the current
/// step's CancellationToken is cancelled immediately. The orchestrator's loop
/// checks the token between every action; it will not start a new step once
/// halted.
///
/// Interruption types (least to most aggressive):
///   <see cref="RequestPause"/>       — pauses after current step completes
///   <see cref="RequestCancel"/>      — stops after current step, no rollback
///   <see cref="EmergencyHalt"/>      — cancels the in-progress step immediately
///   <see cref="RequestRollback"/>    — cancel + undo already-completed steps
/// </summary>
public sealed class HumanInterruptController
{
    private AutomationExecutionContext? _activeContext;
    private readonly object _lock = new();

    // ── Context management ────────────────────────────────────────────────────

    /// <summary>Registers the current execution context so interrupts reach it.</summary>
    public void RegisterContext(AutomationExecutionContext context)
    {
        lock (_lock)
            _activeContext = context;
    }

    /// <summary>Clears the active context when the plan finishes.</summary>
    public void ClearContext()
    {
        lock (_lock)
            _activeContext = null;
    }

    // ── State queries ─────────────────────────────────────────────────────────

    /// <summary>True if the active execution has been interrupted in any way.</summary>
    public bool IsInterrupted => _activeContext?.IsCancelled ?? false;

    /// <summary>True if a plan is currently executing.</summary>
    public bool IsExecuting => _activeContext?.IsRunning ?? false;

    /// <summary>True if execution is currently paused.</summary>
    public bool IsPaused => _activeContext?.IsPaused ?? false;

    // ── Interrupt operations ──────────────────────────────────────────────────

    /// <summary>
    /// Pauses execution after the current step completes.
    /// The plan can be resumed or cancelled from the paused state.
    /// Non-destructive — nothing is undone.
    /// </summary>
    public void RequestPause()
    {
        AutomationExecutionContext? ctx;
        lock (_lock) ctx = _activeContext;
        if (ctx is null || !ctx.IsRunning) return;

        ctx.IsPaused = true;
        ctx.Log("User requested pause.");
        InterruptionOccurred?.Invoke(this, "Paused");
    }

    /// <summary>
    /// Cancels remaining steps after the current step completes.
    /// No rollback — steps already completed remain in effect.
    /// </summary>
    public void RequestCancel()
    {
        AutomationExecutionContext? ctx;
        lock (_lock) ctx = _activeContext;
        if (ctx is null) return;

        ctx.IsCancelled = true;
        ctx.IsPaused    = false;
        ctx.Log("User cancelled execution.");
        InterruptionOccurred?.Invoke(this, "Cancelled");
    }

    /// <summary>
    /// Immediately cancels the CancellationToken for the currently executing step.
    /// The step will abort as soon as it checks its token.
    /// No further steps will start.
    /// </summary>
    public void EmergencyHalt()
    {
        AutomationExecutionContext? ctx;
        lock (_lock) ctx = _activeContext;
        if (ctx is null) return;

        ctx.IsCancelled = true;
        ctx.IsPaused    = false;
        ctx.Log("Emergency halt requested.");

        // Cancel the active step's token — it will abort the in-progress action.
        if (!ctx.CancellationSource.IsCancellationRequested)
            ctx.CancellationSource.Cancel();

        InterruptionOccurred?.Invoke(this, "Emergency halt");
    }

    /// <summary>
    /// Cancels remaining steps AND requests rollback of completed steps.
    /// The orchestrator handles the actual rollback; this flag signals intent.
    /// </summary>
    public void RequestRollback()
    {
        AutomationExecutionContext? ctx;
        lock (_lock) ctx = _activeContext;
        if (ctx is null) return;

        RollbackRequested = true;
        RequestCancel();
        InterruptionOccurred?.Invoke(this, "Rollback requested");
    }

    /// <summary>True if the user has requested rollback of completed steps.</summary>
    public bool RollbackRequested { get; private set; }

    /// <summary>Reset rollback flag when a new plan starts.</summary>
    public void Reset() => RollbackRequested = false;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when any interruption is requested.
    /// The string parameter is a short label ("Paused", "Cancelled", "Emergency halt", etc.).
    /// </summary>
    public event EventHandler<string>? InterruptionOccurred;
}
