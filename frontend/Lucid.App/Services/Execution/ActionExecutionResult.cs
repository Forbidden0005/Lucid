namespace Lucid.Services.Execution;

// ── Status enum ───────────────────────────────────────────────────────────────

/// <summary>
/// Terminal outcome of a single action execution or rollback attempt.
/// </summary>
public enum ActionExecutionStatus
{
    /// <summary>All steps completed and changes were applied successfully.</summary>
    Success              = 0,

    /// <summary>
    /// Most steps completed but some were skipped or partially applied.
    /// The system is improved but not fully remediated. A follow-up action
    /// may be needed.
    /// </summary>
    PartialSuccess       = 1,

    /// <summary>
    /// The action was simulated in dry-run mode. No persistent changes were
    /// made; the log describes what would have happened.
    /// </summary>
    DryRunSuccess        = 2,

    /// <summary>
    /// An error occurred. No lasting changes were made (or changes were
    /// reverted). The log and <see cref="ActionExecutionResult.ErrorDetail"/>
    /// contain diagnostic information.
    /// </summary>
    Failed               = 3,

    /// <summary>
    /// Execution was cancelled (e.g. via <see cref="CancellationToken"/>)
    /// before it completed. The system may be in a partially modified state;
    /// check the log for details.
    /// </summary>
    Cancelled            = 4,

    /// <summary>
    /// The executor requires UAC elevation that the current process does not
    /// have. The engine blocked execution before calling the executor — no
    /// system changes were made.
    /// </summary>
    RequiresElevation    = 5,

    /// <summary>
    /// The executor requires explicit user confirmation that was not provided
    /// in the <see cref="ActionExecutionContext"/>. The engine blocked
    /// execution — no system changes were made.
    /// </summary>
    RequiresConfirmation = 6,

    /// <summary>
    /// No executor is registered for the requested action ID.
    /// Expected during the architecture rollout phase while executors are
    /// still being implemented.
    /// </summary>
    ExecutorNotFound     = 7,
}

// ── Result class ─────────────────────────────────────────────────────────────

/// <summary>
/// Immutable record of the outcome of a single action execution or rollback.
///
/// Always returned by <see cref="IActionExecutionEngine"/> — never thrown.
/// All failure conditions are represented as status codes with human-readable
/// messages so the ViewModel layer can display them directly.
///
/// Creation:
///   Use the static factory methods. The private constructor prevents
///   accidental direct instantiation and ensures required fields are always
///   populated consistently.
///
/// Rollback:
///   When <see cref="CanRollback"/> is <see langword="true"/>, pass
///   <see cref="RollbackToken"/> to
///   <see cref="IActionExecutionEngine.RollbackAsync"/> to undo the changes.
///   The token format is executor-defined (e.g. a restore-point ID, a backup
///   file path, or a registry export path) and is opaque to all other layers.
///
/// Logging:
///   <see cref="Log"/> is always non-null. It is empty for pre-flight
///   failures (RequiresElevation, RequiresConfirmation, ExecutorNotFound)
///   because the executor was never reached.
/// </summary>
public sealed class ActionExecutionResult
{
    // Private constructor — all creation goes through the factory methods below.
    private ActionExecutionResult() { }

    // ── Core outcome ──────────────────────────────────────────────────────────

    /// <summary>Stable action ID this result belongs to.</summary>
    public string               ActionId { get; private init; } = string.Empty;

    /// <summary>Terminal status of this execution attempt.</summary>
    public ActionExecutionStatus Status  { get; private init; }

    /// <summary>
    /// <see langword="true"/> for <see cref="ActionExecutionStatus.Success"/>
    /// and <see cref="ActionExecutionStatus.PartialSuccess"/> only.
    /// Dry-run results are not considered <em>applied</em> — check
    /// <see cref="IsDryRun"/> separately when that distinction matters.
    /// </summary>
    public bool IsSuccess => Status is ActionExecutionStatus.Success
                                    or ActionExecutionStatus.PartialSuccess;

    /// <summary>
    /// <see langword="true"/> when this result reflects a simulation.
    /// Changes were described but not applied.
    /// </summary>
    public bool IsDryRun => Status == ActionExecutionStatus.DryRunSuccess;

    // ── Human-readable outcome ────────────────────────────────────────────────

    /// <summary>
    /// Short outcome message suitable for a toast notification or status chip.
    /// Always non-null.
    /// </summary>
    public string  Message     { get; private init; } = string.Empty;

    /// <summary>
    /// Optional technical detail for failure results — may contain exception
    /// messages, stack traces, or low-level error codes.
    /// <see langword="null"/> for non-failure statuses.
    /// </summary>
    public string? ErrorDetail { get; private init; }

    // ── Timing ────────────────────────────────────────────────────────────────

    /// <summary>Moment this execution attempt began.</summary>
    public DateTimeOffset ExecutedAt { get; private init; }

    /// <summary>
    /// Wall-clock duration of the execution attempt.
    /// <see cref="TimeSpan.Zero"/> for pre-flight failures.
    /// </summary>
    public TimeSpan Duration { get; private init; }

    // ── Rollback metadata ─────────────────────────────────────────────────────

    /// <summary>
    /// <see langword="true"/> when the executor captured enough state to undo
    /// this execution. Only ever set on <see cref="ActionExecutionStatus.Success"/>
    /// results — partial successes and failures do not support rollback.
    /// </summary>
    public bool    CanRollback   { get; private init; }

    /// <summary>
    /// Opaque executor-defined string that identifies the rollback snapshot.
    /// Pass this to <see cref="IActionExecutionEngine.RollbackAsync"/> to
    /// undo the action.
    /// <see langword="null"/> when <see cref="CanRollback"/> is
    /// <see langword="false"/>.
    /// </summary>
    public string? RollbackToken { get; private init; }

    // ── Execution log ─────────────────────────────────────────────────────────

    /// <summary>
    /// Ordered sequence of entries written by the engine and executor.
    /// Always non-null. Empty for pre-flight failures (the executor was
    /// never called).
    /// </summary>
    public IReadOnlyList<ActionLogEntry> Log { get; private init; } = [];

    // ── Factory methods — success paths ───────────────────────────────────────

    /// <summary>
    /// Creates a full-success result.
    /// Pass a non-null <paramref name="rollbackToken"/> when the executor
    /// captured state that allows the action to be undone.
    /// </summary>
    public static ActionExecutionResult Succeeded(
        string                       actionId,
        string                       message,
        TimeSpan                     duration,
        IReadOnlyList<ActionLogEntry> log,
        string?                      rollbackToken = null) => new()
    {
        ActionId      = actionId,
        Status        = ActionExecutionStatus.Success,
        Message       = message,
        ExecutedAt    = DateTimeOffset.Now,
        Duration      = duration,
        Log           = log,
        CanRollback   = rollbackToken is not null,
        RollbackToken = rollbackToken,
    };

    /// <summary>
    /// Creates a partial-success result for actions that completed with caveats
    /// (e.g. some files deleted but others skipped due to locks).
    /// Pass a non-null <paramref name="rollbackToken"/> when the executor staged
    /// the files it did remove and rollback is therefore possible for the subset
    /// of changes that were applied.
    /// </summary>
    public static ActionExecutionResult PartiallySucceeded(
        string                       actionId,
        string                       message,
        TimeSpan                     duration,
        IReadOnlyList<ActionLogEntry> log,
        string?                      rollbackToken = null) => new()
    {
        ActionId      = actionId,
        Status        = ActionExecutionStatus.PartialSuccess,
        Message       = message,
        ExecutedAt    = DateTimeOffset.Now,
        Duration      = duration,
        Log           = log,
        CanRollback   = rollbackToken is not null,
        RollbackToken = rollbackToken,
    };

    /// <summary>
    /// Creates a dry-run result — action was simulated, no changes made.
    /// </summary>
    public static ActionExecutionResult DryRunCompleted(
        string                       actionId,
        string                       message,
        TimeSpan                     duration,
        IReadOnlyList<ActionLogEntry> log) => new()
    {
        ActionId   = actionId,
        Status     = ActionExecutionStatus.DryRunSuccess,
        Message    = message,
        ExecutedAt = DateTimeOffset.Now,
        Duration   = duration,
        Log        = log,
    };

    // ── Factory methods — failure paths ───────────────────────────────────────

    /// <summary>
    /// Creates a failure result. Pass <paramref name="errorDetail"/> with
    /// technical information (e.g. exception.ToString()) for the log panel.
    /// </summary>
    public static ActionExecutionResult Failed(
        string                       actionId,
        string                       message,
        TimeSpan                     duration,
        IReadOnlyList<ActionLogEntry> log,
        string?                      errorDetail = null) => new()
    {
        ActionId    = actionId,
        Status      = ActionExecutionStatus.Failed,
        Message     = message,
        ErrorDetail = errorDetail,
        ExecutedAt  = DateTimeOffset.Now,
        Duration    = duration,
        Log         = log,
    };

    /// <summary>
    /// Creates a cancellation result for when a
    /// <see cref="CancellationToken"/> fires during execution.
    /// </summary>
    public static ActionExecutionResult Cancelled(
        string                       actionId,
        string                       message,
        TimeSpan                     duration,
        IReadOnlyList<ActionLogEntry> log) => new()
    {
        ActionId   = actionId,
        Status     = ActionExecutionStatus.Cancelled,
        Message    = message,
        ExecutedAt = DateTimeOffset.Now,
        Duration   = duration,
        Log        = log,
    };

    // ── Factory methods — pre-flight failure paths (executor never called) ────

    /// <summary>
    /// Creates a pre-flight failure result when the executor requires
    /// administrator elevation that the process does not currently have.
    /// The executor was not called; no system changes were made.
    /// </summary>
    public static ActionExecutionResult RequiresElevation(string actionId) => new()
    {
        ActionId   = actionId,
        Status     = ActionExecutionStatus.RequiresElevation,
        Message    = "This action requires administrator privileges. " +
                     "Re-launch Lucid as administrator and try again.",
        ExecutedAt = DateTimeOffset.Now,
    };

    /// <summary>
    /// Creates a pre-flight failure result when the action requires explicit
    /// user confirmation that was not provided in the context.
    /// The executor was not called; no system changes were made.
    /// </summary>
    public static ActionExecutionResult RequiresConfirmation(string actionId) => new()
    {
        ActionId   = actionId,
        Status     = ActionExecutionStatus.RequiresConfirmation,
        Message    = "This action requires your explicit confirmation before it can run.",
        ExecutedAt = DateTimeOffset.Now,
    };

    /// <summary>
    /// Creates a pre-flight failure result when no executor is registered for
    /// the requested action ID. Expected during development until all action
    /// executors have been implemented.
    /// </summary>
    public static ActionExecutionResult ExecutorNotFound(string actionId) => new()
    {
        ActionId   = actionId,
        Status     = ActionExecutionStatus.ExecutorNotFound,
        Message    = $"No executor is registered for '{actionId}'. " +
                     $"This action is not yet implemented.",
        ExecutedAt = DateTimeOffset.Now,
    };
}
