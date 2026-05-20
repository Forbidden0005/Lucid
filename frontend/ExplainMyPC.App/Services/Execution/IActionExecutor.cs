namespace ExplainMyPC.Services.Execution;

// ── Privilege enum ────────────────────────────────────────────────────────────

/// <summary>
/// The Windows privilege level required to run an executor.
///
/// Checked by <see cref="ActionExecutionEngine"/> during pre-flight validation.
/// Executors that require <see cref="Administrator"/> will receive
/// <see cref="ActionExecutionStatus.RequiresElevation"/> from the engine
/// when <see cref="ActionExecutionContext.IsElevated"/> is
/// <see langword="false"/>.
/// </summary>
public enum ActionPrivilegeLevel
{
    /// <summary>
    /// Runs under the current user account without elevation.
    /// Suitable for UI automation, per-user registry keys, and file
    /// operations within the user profile.
    /// </summary>
    Standard      = 0,

    /// <summary>
    /// Requires a UAC-elevated (administrator) process.
    /// Needed for system-wide registry edits, driver changes, service
    /// management, and writing to protected directories.
    /// </summary>
    Administrator = 1,
}

// ── Executor interface ────────────────────────────────────────────────────────

/// <summary>
/// Contract for a single concrete action executor.
///
/// Each executor handles exactly one <see cref="ActionId"/> and encapsulates
/// all platform-specific logic needed to carry out (or simulate) that action
/// and optionally undo it.
///
/// Authoring guidance:
///   • One class per action ID — keep executors small and focused.
///   • Executors are stateless across calls. Per-call state belongs in local
///     variables or in the <see cref="ActionExecutionContext.Log"/>.
///   • Write progress to <c>context.Log</c> throughout execution so the user
///     sees a meaningful audit trail.
///   • Always honour <see cref="ActionExecutionContext.IsDryRun"/>:
///     simulate every side effect, log what would have happened, and return
///     <see cref="ActionExecutionResult.DryRunCompleted"/> without touching
///     the system.
///   • Never throw from <see cref="ExecuteAsync"/> or <see cref="RollbackAsync"/>.
///     Catch all exceptions internally and return a
///     <see cref="ActionExecutionResult.Failed"/> result instead.
///     The engine catches unhandled exceptions as a last resort, but relying
///     on that is considered a bug in the executor.
///   • Allocate executor instances as singletons (registered once in
///     <see cref="ActionExecutorRegistry"/>) — no per-call heap allocations.
/// </summary>
public interface IActionExecutor
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The action ID this executor handles.
    /// Must exactly match the <c>Id</c> of the <see cref="SystemAction"/> it
    /// was designed for (e.g. <c>"action.disk.clean-temp-files"</c>).
    /// </summary>
    string ActionId { get; }

    // ── Capability declarations ───────────────────────────────────────────────

    /// <summary>
    /// Windows privilege level the executor requires.
    /// The engine enforces this before calling <see cref="ExecuteAsync"/>.
    /// </summary>
    ActionPrivilegeLevel RequiredPrivilege { get; }

    /// <summary>
    /// <see langword="true"/> when this executor requires the user to confirm
    /// before the action runs.
    ///
    /// Should mirror the <c>RequiresConfirmation</c> field on the corresponding
    /// <see cref="SystemAction"/>. The engine enforces this flag: if it is
    /// <see langword="true"/> and <see cref="ActionExecutionContext.ConfirmationGranted"/>
    /// is <see langword="false"/>, the engine returns
    /// <see cref="ActionExecutionStatus.RequiresConfirmation"/> before calling
    /// the executor.
    /// </summary>
    bool RequiresConfirmation { get; }

    /// <summary>
    /// <see langword="true"/> when <see cref="ExecuteAsync"/> supports
    /// dry-run mode (i.e. it respects <see cref="ActionExecutionContext.IsDryRun"/>).
    ///
    /// When <see langword="false"/> and a dry-run is requested, the engine
    /// returns a synthetic <see cref="ActionExecutionStatus.DryRunSuccess"/>
    /// without calling the executor. Most executors should return
    /// <see langword="true"/> and implement the dry-run branch.
    /// </summary>
    bool SupportsDryRun { get; }

    /// <summary>
    /// <see langword="true"/> when this executor can undo a previously
    /// applied execution via <see cref="RollbackAsync"/>.
    ///
    /// Only executors that return a non-null
    /// <see cref="ActionExecutionResult.RollbackToken"/> in their success
    /// result should set this to <see langword="true"/>.
    /// </summary>
    bool SupportsRollback { get; }

    // ── Execution ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the action (or simulates it when
    /// <see cref="ActionExecutionContext.IsDryRun"/> is <see langword="true"/>).
    ///
    /// Rules:
    ///   • Never throw — catch all exceptions and return
    ///     <see cref="ActionExecutionResult.Failed"/>.
    ///   • Write progress entries to <paramref name="context"/>.Log.
    ///   • Call <see cref="ActionExecutionLog.Build"/> on <paramref name="context"/>.Log
    ///     exactly once when building the result to seal the log.
    ///   • Honour <paramref name="cancellationToken"/>: check it at
    ///     appropriate intervals and return
    ///     <see cref="ActionExecutionResult.Cancelled"/> when signalled.
    /// </summary>
    Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionContext context,
        CancellationToken      cancellationToken = default);

    // ── Rollback ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Undoes a previously applied execution identified by
    /// <paramref name="rollbackToken"/>.
    ///
    /// Only called by the engine when <see cref="SupportsRollback"/> is
    /// <see langword="true"/> and the original <see cref="ActionExecutionResult"/>
    /// carried a non-null <see cref="ActionExecutionResult.RollbackToken"/>.
    ///
    /// Rules:
    ///   • Same as <see cref="ExecuteAsync"/>: never throw, write to the log,
    ///     call Build exactly once.
    ///   • If <see cref="SupportsRollback"/> is <see langword="false"/>, this
    ///     method may throw <see cref="NotSupportedException"/>; the engine
    ///     will catch it and return a failure result.
    /// </summary>
    Task<ActionExecutionResult> RollbackAsync(
        string                rollbackToken,
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default);
}
