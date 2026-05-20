namespace ExplainMyPC.Services.Execution;

/// <summary>
/// Public contract of the action execution engine.
///
/// The engine is the single authoritative entry point for all action
/// execution. It sits between the ViewModel layer and the
/// <see cref="IActionExecutor"/> implementations, enforcing safety rules
/// (elevation, confirmation, dry-run capability) before dispatching.
///
/// Responsibilities:
///   • Executor discovery — routes an action ID to its registered executor.
///   • Pre-flight validation — elevation, confirmation, dry-run capability.
///   • Timing — measures wall-clock duration for every call.
///   • Exception containment — catches unhandled executor exceptions and
///     wraps them as <see cref="ActionExecutionStatus.Failed"/> results.
///   • Audit logging — writes a dispatch entry to every context's log.
///
/// What the engine does NOT do:
///   • It never performs file I/O, registry access, or any system modification.
///   • It never shows UI — confirmation dialogs are the ViewModel's concern.
///   • It never manages executor lifetime — executors are singletons registered
///     once in <see cref="ActionExecutorRegistry"/>.
///
/// Callers (ViewModels, background services) should always call the engine
/// rather than invoking <see cref="IActionExecutor"/> directly, so that all
/// pre-flight checks and logging are applied consistently.
/// </summary>
public interface IActionExecutionEngine
{
    /// <summary>
    /// Executes the action identified by <paramref name="actionId"/> using the
    /// supplied <paramref name="context"/>.
    ///
    /// Pre-flight order (engine stops and returns early at the first failure):
    ///   1. Executor lookup   → <see cref="ActionExecutionStatus.ExecutorNotFound"/>
    ///   2. Elevation check   → <see cref="ActionExecutionStatus.RequiresElevation"/>
    ///   3. Confirmation check → <see cref="ActionExecutionStatus.RequiresConfirmation"/>
    ///   4. Dry-run capability → synthetic <see cref="ActionExecutionStatus.DryRunSuccess"/>
    ///   5. Executor dispatch
    ///   6. Exception containment → <see cref="ActionExecutionStatus.Failed"/>
    ///
    /// Always returns a result — never throws.
    /// </summary>
    /// <param name="actionId">
    ///   Stable dot-separated identifier matching an
    ///   <see cref="IActionExecutor.ActionId"/> registered in the
    ///   <see cref="ActionExecutorRegistry"/>.
    /// </param>
    /// <param name="context">
    ///   Execution context carrying user intent and the mutable log builder.
    ///   Create a fresh context per call; never reuse across calls.
    /// </param>
    Task<ActionExecutionResult> ExecuteAsync(
        string                actionId,
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default);

    /// <summary>
    /// Rolls back a previously executed action using a token captured from
    /// the original <see cref="ActionExecutionResult.RollbackToken"/>.
    ///
    /// Pre-flight:
    ///   1. Executor lookup
    ///   2. Rollback support check → <see cref="ActionExecutionStatus.Failed"/>
    ///      when <see cref="IActionExecutor.SupportsRollback"/> is false
    ///   3. Elevation check
    ///   4. Executor dispatch (RollbackAsync)
    ///   5. Exception containment
    ///
    /// Always returns a result — never throws.
    /// </summary>
    Task<ActionExecutionResult> RollbackAsync(
        string                actionId,
        string                rollbackToken,
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default);

    /// <summary>
    /// Returns <see langword="true"/> when an executor is registered for
    /// <paramref name="actionId"/>. Does not verify privilege or confirmation.
    /// Use to drive UI state (e.g. enable/disable an "Execute" button).
    /// </summary>
    bool IsSupported(string actionId);
}
