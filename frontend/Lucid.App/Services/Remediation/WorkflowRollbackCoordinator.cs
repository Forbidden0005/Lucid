using Lucid.Services.Execution;

namespace Lucid.Services.Remediation;

/// <summary>
/// Tracks rollback tokens from each executed step and coordinates
/// rollback execution in reverse order.
///
/// Philosophy:
///   Rollback is always offered, never forced (except under RecoveryAutomation
///   when stabilization verifiably degrades).
///   Each step's rollback token is stored immediately after success.
/// </summary>
public sealed class WorkflowRollbackCoordinator
{
    private readonly IActionExecutionEngine              _engine;
    private readonly List<(string ActionId, string Token, string StepName)> _rollbackStack = [];

    public WorkflowRollbackCoordinator(IActionExecutionEngine engine)
    {
        _engine = engine;
    }

    // ── Token registration ────────────────────────────────────────────────────

    /// <summary>
    /// Records a rollback token from a successfully executed step.
    /// Only call this when <see cref="ActionExecutionResult.CanRollback"/> is true.
    /// </summary>
    public void RegisterRollbackToken(
        string actionId,
        string rollbackToken,
        string stepName)
    {
        _rollbackStack.Add((actionId, rollbackToken, stepName));
    }

    /// <summary>True when at least one step has a registered rollback token.</summary>
    public bool HasRollbackTokens => _rollbackStack.Count > 0;

    /// <summary>Number of steps that can be rolled back.</summary>
    public int RollbackCount => _rollbackStack.Count;

    // ── Rollback execution ────────────────────────────────────────────────────

    /// <summary>
    /// Executes rollback for all registered steps in reverse execution order.
    /// Returns a list of step names that were successfully rolled back.
    ///
    /// Never throws — all exceptions are caught per step.
    /// </summary>
    public async Task<IReadOnlyList<string>> ExecuteRollbackAsync(CancellationToken ct = default)
    {
        var rolledBack = new List<string>();

        // Execute in reverse order (LIFO)
        for (var i = _rollbackStack.Count - 1; i >= 0; i--)
        {
            var (actionId, token, stepName) = _rollbackStack[i];

            try
            {
                var context = new ActionExecutionContext
                {
                    RequestedBy        = "rollback-coordinator",
                    ConfirmationGranted = true,
                };

                var result = await _engine.RollbackAsync(
                    actionId, token, context, ct).ConfigureAwait(false);

                if (result.IsSuccess)
                    rolledBack.Add(stepName);
            }
            catch
            {
                // Continue rolling back other steps even if one fails
            }
        }

        _rollbackStack.Clear();
        return rolledBack;
    }

    /// <summary>Clears the rollback stack without executing rollback.</summary>
    public void Clear() => _rollbackStack.Clear();

    /// <summary>
    /// Returns the names of all steps that can be rolled back (in reverse order).
    /// </summary>
    public IReadOnlyList<string> GetRollbackableStepNames() =>
        _rollbackStack.Select(r => r.StepName).Reverse().ToList();
}
