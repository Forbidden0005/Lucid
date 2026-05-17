using System.Diagnostics;

namespace ExplainMyPC.Services.Execution;

/// <summary>
/// Concrete action execution engine.
///
/// See <see cref="IActionExecutionEngine"/> for the full contract.
///
/// Execution sequence for <see cref="ExecuteAsync"/>:
///
///   ┌─ Pre-flight ─────────────────────────────────────────────────────────┐
///   │ 1. Look up executor.   Missing → return ExecutorNotFound.            │
///   │ 2. Elevation check.    Needs admin + not elevated → RequiresElevation│
///   │ 3. Confirmation check. Needs confirm + not granted → RequiresConfirm │
///   │ 4. Dry-run gate.       IsDryRun + !SupportsDryRun → synthetic        │
///   │                        DryRunSuccess without calling executor.       │
///   └──────────────────────────────────────────────────────────────────────┘
///   ┌─ Dispatch ────────────────────────────────────────────────────────────┐
///   │ 5. Write dispatch entry to context.Log.                              │
///   │ 6. Start Stopwatch.                                                   │
///   │ 7. await executor.ExecuteAsync(context, ct).                         │
///   │ 8. Stop Stopwatch. Return executor's result.                          │
///   └──────────────────────────────────────────────────────────────────────┘
///   ┌─ Exception containment ───────────────────────────────────────────────┐
///   │  OperationCanceledException → Cancelled result.                       │
///   │  Any other exception        → Failed result with errorDetail.         │
///   └──────────────────────────────────────────────────────────────────────┘
///
/// The engine itself never performs file I/O, registry access, or any other
/// system modification. All side effects live in the executor implementations.
/// </summary>
public sealed class ActionExecutionEngine : IActionExecutionEngine
{
    private readonly ActionExecutorRegistry _registry;

    /// <param name="registry">
    /// Pre-populated registry of executor implementations.
    /// Typically provided by <see cref="AppServices"/>.
    /// </param>
    public ActionExecutionEngine(ActionExecutorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    // ── IActionExecutionEngine ────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ActionExecutionResult> ExecuteAsync(
        string                actionId,
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ArgumentNullException.ThrowIfNull(context);

        // ── 1. Executor lookup ────────────────────────────────────────────────
        if (!_registry.TryGetExecutor(actionId, out var executor) || executor is null)
            return ActionExecutionResult.ExecutorNotFound(actionId);

        // ── 2. Elevation check ────────────────────────────────────────────────
        if (executor.RequiredPrivilege == ActionPrivilegeLevel.Administrator
            && !context.IsElevated)
            return ActionExecutionResult.RequiresElevation(actionId);

        // ── 3. Confirmation check ─────────────────────────────────────────────
        // The engine enforces the confirmation gate so individual executors
        // don't have to repeat the check. The ViewModel is responsible for
        // showing the confirmation dialog and setting ConfirmationGranted=true
        // before calling the engine for any action that requires it.
        if (executor.RequiresConfirmation && !context.ConfirmationGranted)
            return ActionExecutionResult.RequiresConfirmation(actionId);

        // ── 4. Dry-run capability gate ────────────────────────────────────────
        if (context.IsDryRun && !executor.SupportsDryRun)
        {
            context.Log.Info(
                $"[Engine] Dry-run requested for '{actionId}' but the executor " +
                $"does not implement dry-run simulation. Returning synthetic success.");
            return ActionExecutionResult.DryRunCompleted(
                actionId,
                "Dry-run simulation is not supported for this action. No changes were made.",
                TimeSpan.Zero,
                context.Log.Build());
        }

        // ── 5. Dispatch ───────────────────────────────────────────────────────
        context.Log.Info(
            $"[Engine] Dispatching '{actionId}' " +
            $"(requestedBy={context.RequestedBy}, " +
            $"dryRun={context.IsDryRun}, " +
            $"elevated={context.IsElevated}).");

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await executor
                .ExecuteAsync(context, cancellationToken)
                .ConfigureAwait(false);

            sw.Stop();
            return result;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            context.Log.Warn(
                $"[Engine] '{actionId}' was cancelled after {sw.ElapsedMilliseconds} ms.");
            return ActionExecutionResult.Cancelled(
                actionId,
                "The action was cancelled before it completed.",
                sw.Elapsed,
                context.Log.Build());
        }
        catch (Exception ex)
        {
            sw.Stop();
            context.Log.Error(
                $"[Engine] Unhandled exception in executor for '{actionId}' " +
                $"after {sw.ElapsedMilliseconds} ms: {ex.GetType().Name}: {ex.Message}");
            return ActionExecutionResult.Failed(
                actionId,
                "An unexpected error occurred. No changes were made.",
                sw.Elapsed,
                context.Log.Build(),
                errorDetail: ex.ToString());
        }
    }

    /// <inheritdoc />
    public async Task<ActionExecutionResult> RollbackAsync(
        string                actionId,
        string                rollbackToken,
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rollbackToken);
        ArgumentNullException.ThrowIfNull(context);

        // ── 1. Executor lookup ────────────────────────────────────────────────
        if (!_registry.TryGetExecutor(actionId, out var executor) || executor is null)
            return ActionExecutionResult.ExecutorNotFound(actionId);

        // ── 2. Rollback support check ─────────────────────────────────────────
        if (!executor.SupportsRollback)
        {
            context.Log.Warn(
                $"[Engine] Rollback requested for '{actionId}' but the executor " +
                $"does not support rollback.");
            return ActionExecutionResult.Failed(
                actionId,
                "This action does not support rollback.",
                TimeSpan.Zero,
                context.Log.Build());
        }

        // ── 3. Elevation check ────────────────────────────────────────────────
        // Rollback may require the same privilege level as the original action.
        if (executor.RequiredPrivilege == ActionPrivilegeLevel.Administrator
            && !context.IsElevated)
            return ActionExecutionResult.RequiresElevation(actionId);

        // ── 4. Dispatch ───────────────────────────────────────────────────────
        context.Log.Info(
            $"[Engine] Rolling back '{actionId}' " +
            $"(token='{rollbackToken}', requestedBy={context.RequestedBy}).");

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await executor
                .RollbackAsync(rollbackToken, context, cancellationToken)
                .ConfigureAwait(false);

            sw.Stop();
            return result;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            context.Log.Warn(
                $"[Engine] Rollback of '{actionId}' was cancelled after " +
                $"{sw.ElapsedMilliseconds} ms.");
            return ActionExecutionResult.Cancelled(
                actionId,
                "The rollback was cancelled before it completed.",
                sw.Elapsed,
                context.Log.Build());
        }
        catch (Exception ex)
        {
            sw.Stop();
            context.Log.Error(
                $"[Engine] Unhandled exception during rollback of '{actionId}' " +
                $"after {sw.ElapsedMilliseconds} ms: {ex.GetType().Name}: {ex.Message}");
            return ActionExecutionResult.Failed(
                actionId,
                "An unexpected error occurred during rollback.",
                sw.Elapsed,
                context.Log.Build(),
                errorDetail: ex.ToString());
        }
    }

    /// <inheritdoc />
    public bool IsSupported(string actionId) =>
        _registry.IsRegistered(actionId);
}
