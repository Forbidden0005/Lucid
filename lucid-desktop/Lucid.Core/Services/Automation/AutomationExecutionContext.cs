namespace Lucid.Services.Automation;

/// <summary>
/// Mutable runtime context for an in-progress automation plan execution.
///
/// Created by <see cref="AutomationOrchestrator"/> when a plan is approved
/// and destroyed when the plan completes, is cancelled, or fails.
///
/// NOT thread-safe — all access must be on the UI thread.
/// The orchestrator enforces this by marshalling all state changes through
/// the DispatcherQueue before updating this context.
/// </summary>
public sealed class AutomationExecutionContext
{
    // ── Plan identity ─────────────────────────────────────────────────────────

    public string PlanId { get; }

    // ── Mode ──────────────────────────────────────────────────────────────────

    /// <summary>Mode active when this execution started. Cannot change mid-execution.</summary>
    public AutomationMode Mode { get; }

    // ── State ─────────────────────────────────────────────────────────────────

    public bool IsRunning   { get; set; }
    public bool IsPaused    { get; set; }
    public bool IsCancelled { get; set; }
    public bool IsFailed    { get; set; }
    public bool IsComplete  => !IsRunning && !IsPaused && !IsCancelled && !IsFailed
                                && CompletedStepIds.Count > 0;

    /// <summary>Index of the step currently executing or about to execute.</summary>
    public int CurrentStepIndex { get; set; }

    // ── Interrupt control ─────────────────────────────────────────────────────

    /// <summary>
    /// Token source used to signal cancellation to the executing step.
    /// EmergencyHalt() calls Cancel() on this source immediately.
    /// </summary>
    public CancellationTokenSource CancellationSource { get; } = new();

    /// <summary>Shorthand — cancelled token derived from the source.</summary>
    public CancellationToken CancellationToken => CancellationSource.Token;

    // ── Audit log ─────────────────────────────────────────────────────────────

    /// <summary>Chronological narration log — every step start/complete/fail is appended here.</summary>
    public List<string> ExecutionLog     { get; } = [];

    /// <summary>Step IDs that have been completed — used for rollback planning.</summary>
    public List<string> CompletedStepIds { get; } = [];

    /// <summary>Step IDs that failed — tracked for diagnostics and retry logic.</summary>
    public List<string> FailedStepIds   { get; } = [];

    // ── Confirmation gate ─────────────────────────────────────────────────────

    /// <summary>
    /// Semaphore used to pause execution while awaiting user confirmation.
    /// The orchestrator releases this when the user approves or denies.
    /// </summary>
    public SemaphoreSlim ConfirmationGate { get; } = new(0, 1);

    /// <summary>Set to true when the user taps Approve in the confirmation card.</summary>
    public bool ConfirmationApproved { get; set; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public AutomationExecutionContext(string planId, AutomationMode mode)
    {
        PlanId = planId;
        Mode   = mode;
    }

    // ── Log helpers ───────────────────────────────────────────────────────────

    public void Log(string message)
        => ExecutionLog.Add($"[{DateTimeOffset.Now:HH:mm:ss}] {message}");

    public void LogStepStarted(AutomationStep step)
        => Log($"Starting: {step.Title}");

    public void LogStepCompleted(AutomationStep step)
    {
        Log($"Completed: {step.Title}");
        CompletedStepIds.Add(step.Id);
    }

    public void LogStepFailed(AutomationStep step, string error)
    {
        Log($"Failed: {step.Title} — {error}");
        FailedStepIds.Add(step.Id);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        CancellationSource.Dispose();
        ConfirmationGate.Dispose();
    }
}
