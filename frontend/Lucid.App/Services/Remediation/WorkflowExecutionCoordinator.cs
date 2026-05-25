using Lucid.Helpers;
using Lucid.Services.Execution;
using Lucid.Services.Governance;
using Lucid.Services.Intelligence;

namespace Lucid.Services.Remediation;

/// <summary>
/// Executes a <see cref="RemediationWorkflow"/> step-by-step, enforcing safety
/// constraints between each step and collecting rollback tokens.
///
/// Design principles:
///   • Each step calls <see cref="IActionExecutionEngine.ExecuteAsync"/> once.
///   • Safety pre-check runs before each step.
///   • Rollback tokens are accumulated after each successful step.
///   • Cancellation is propagated to every async operation.
///   • All progress updates are pushed through <see cref="IProgress{T}"/>.
///   • Never throws — all exceptions are surfaced as WorkflowStatus.Failed.
/// </summary>
public sealed class WorkflowExecutionCoordinator
{
    private readonly IActionExecutionEngine  _engine;
    private readonly IRuntimeGovernanceService _governance;

    public WorkflowExecutionCoordinator(
        IActionExecutionEngine    engine,
        IRuntimeGovernanceService governance)
    {
        _engine     = engine;
        _governance = governance;
    }

    // ── Execution ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes all steps in the workflow sequentially.
    ///
    /// <paramref name="progress"/> is invoked after every state change so the
    /// caller can marshal updates to the UI thread.
    ///
    /// The returned <see cref="WorkflowExecutionState"/> reflects the final
    /// status after all steps (or early halt).
    ///
    /// Never throws — all exceptions result in WorkflowStatus.Failed.
    /// </summary>
    public async Task<WorkflowExecutionState> ExecuteAsync(
        RemediationWorkflow              workflow,
        StabilizationSnapshot            beforeSnapshot,
        AutomationTrustLevel             trustLevel,
        IProgress<WorkflowExecutionState>? progress    = null,
        CancellationToken                ct            = default)
    {
        var rollback = new WorkflowRollbackCoordinator(_engine);
        var state    = new WorkflowExecutionState
        {
            WorkflowId    = workflow.WorkflowId,
            Workflow      = workflow,
            Status        = WorkflowStatus.Running,
            CurrentPhase  = WorkflowPhase.Execution,
            BeforeSnapshot = beforeSnapshot,
            StartedAt     = DateTimeOffset.Now,
        };

        // Initialise step records
        foreach (var step in workflow.Steps)
        {
            state.StepResults.Add(new StepExecutionRecord
            {
                StepId    = step.StepId,
                StepName  = step.Name,
                ActionKey = step.ActionKey,
                Outcome   = StepOutcome.Pending,
            });
        }

        Report(progress, state);

        try
        {
            // ── Execute steps ─────────────────────────────────────────────────
            for (var i = 0; i < workflow.Steps.Count; i++)
            {
                if (ct.IsCancellationRequested)
                {
                    state.Status     = WorkflowStatus.Cancelled;
                    state.StatusText = "Workflow cancelled by user.";
                    state.CompletedAt = DateTimeOffset.Now;
                    Report(progress, state);
                    return state;
                }

                var step       = workflow.Steps[i];
                var stepRecord = state.StepResults[i];

                // ── Safety pre-check ──────────────────────────────────────────
                var safetyCheck = SafetyConstraintEngine.CheckStepCondition(
                    step, state, _governance.CurrentMode);

                if (safetyCheck.Status == SafetyCheckStatus.Blocked)
                {
                    if (step.IsOptional)
                    {
                        stepRecord.Outcome       = StepOutcome.Skipped;
                        stepRecord.StatusMessage = $"Skipped: {safetyCheck.Reason}";
                        Report(progress, state);
                        continue;
                    }

                    // Hard block on required step
                    state.Status     = WorkflowStatus.Halted;
                    state.HaltReason = safetyCheck.Reason;
                    state.StatusText = $"Halted: {safetyCheck.Reason}";
                    state.CompletedAt = DateTimeOffset.Now;
                    Report(progress, state);

                    // Trigger auto-rollback if recovery automation is enabled
                    if (trustLevel == AutomationTrustLevel.RecoveryAutomation
                        && rollback.HasRollbackTokens)
                    {
                        await rollback.ExecuteRollbackAsync(ct).ConfigureAwait(false);
                        state.Status     = WorkflowStatus.RolledBack;
                        state.StatusText = "Auto-rolled back due to safety halt.";
                        Report(progress, state);
                    }

                    return state;
                }

                // ── Execute step ──────────────────────────────────────────────
                stepRecord.Outcome    = StepOutcome.Running;
                stepRecord.StartedAt  = DateTimeOffset.Now;
                state.CurrentPhase    = step.Phase;
                state.StatusText      = $"Running: {step.Name}";
                Report(progress, state);

                var context = new ActionExecutionContext
                {
                    RequestedBy         = "workflow-coordinator",
                    ConfirmationGranted  = step.RequiresConfirmation
                                          ? (trustLevel >= AutomationTrustLevel.SupervisedAutomation)
                                          : true,
                    Parameters          = step.Parameters
                                          ?? new Dictionary<string, string>(),
                };

                var result = await _engine.ExecuteAsync(step.ActionKey, context, ct)
                    .ConfigureAwait(false);

                stepRecord.CompletedAt  = DateTimeOffset.Now;
                stepRecord.StatusMessage = result.Message;

                // ── Map result to step outcome ─────────────────────────────────
                if (result.IsSuccess)
                {
                    stepRecord.Outcome = result.Status == ActionExecutionStatus.PartialSuccess
                        ? StepOutcome.PartialSuccess
                        : StepOutcome.Succeeded;

                    // Capture rollback token if available
                    if (result.CanRollback && result.RollbackToken is not null)
                    {
                        stepRecord.CanRollback   = true;
                        stepRecord.RollbackToken = result.RollbackToken;
                        rollback.RegisterRollbackToken(
                            step.ActionKey, result.RollbackToken, step.Name);
                    }
                }
                else if (result.Status == ActionExecutionStatus.Cancelled)
                {
                    stepRecord.Outcome  = StepOutcome.Failed;
                    state.Status        = WorkflowStatus.Cancelled;
                    state.StatusText    = "Workflow cancelled.";
                    state.CompletedAt   = DateTimeOffset.Now;
                    Report(progress, state);
                    return state;
                }
                else if (result.Status is ActionExecutionStatus.RequiresElevation
                                       or ActionExecutionStatus.ExecutorNotFound)
                {
                    // Block or skip depending on whether step is optional
                    if (step.IsOptional)
                    {
                        stepRecord.Outcome       = StepOutcome.Skipped;
                        stepRecord.StatusMessage = result.Message;
                    }
                    else
                    {
                        stepRecord.Outcome = StepOutcome.Failed;
                        state.Status       = WorkflowStatus.Halted;
                        state.HaltReason   = result.Message;
                        state.StatusText   = $"Halted: {result.Message}";
                        state.CompletedAt  = DateTimeOffset.Now;
                        Report(progress, state);
                        return state;
                    }
                }
                else
                {
                    stepRecord.Outcome = StepOutcome.Failed;

                    if (!step.IsOptional)
                    {
                        state.Status      = WorkflowStatus.Halted;
                        state.HaltReason  = result.Message;
                        state.StatusText  = $"Halted: {result.Message}";
                        state.CompletedAt = DateTimeOffset.Now;
                        Report(progress, state);
                        return state;
                    }
                }

                Report(progress, state);
            }

            // ── All steps done ────────────────────────────────────────────────
            state.CurrentPhase = WorkflowPhase.Validation;
            state.Status       = WorkflowStatus.Completed;
            state.StatusText   = "Workflow completed.";
            state.CompletedAt  = DateTimeOffset.Now;
            Report(progress, state);
        }
        catch (OperationCanceledException)
        {
            state.Status      = WorkflowStatus.Cancelled;
            state.StatusText  = "Workflow cancelled.";
            state.CompletedAt = DateTimeOffset.Now;
            Report(progress, state);
        }
        catch (Exception ex)
        {
            state.Status      = WorkflowStatus.Failed;
            state.HaltReason  = ex.Message;
            state.StatusText  = $"Workflow failed: {ex.Message}";
            state.CompletedAt = DateTimeOffset.Now;
            Report(progress, state);
        }

        return state;
    }

    // ── Rollback ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Rolls back a completed or halted workflow using the accumulated rollback tokens.
    /// Returns names of steps that were successfully rolled back.
    /// </summary>
    public async Task<WorkflowExecutionState> RollbackAsync(
        WorkflowExecutionState             state,
        IProgress<WorkflowExecutionState>? progress = null,
        CancellationToken                  ct       = default)
    {
        state.Status     = WorkflowStatus.Running;
        state.StatusText = "Rolling back…";
        Report(progress, state);

        var rollback = new WorkflowRollbackCoordinator(_engine);

        // Re-register tokens from completed steps
        foreach (var record in state.StepResults.Where(r => r.CanRollback
            && r.RollbackToken is not null))
        {
            rollback.RegisterRollbackToken(record.ActionKey, record.RollbackToken!, record.StepName);
        }

        var rolledBackSteps = await rollback.ExecuteRollbackAsync(ct).ConfigureAwait(false);

        // Mark rolled-back step records
        foreach (var record in state.StepResults.Where(r => r.CanRollback))
        {
            if (rolledBackSteps.Contains(record.StepName))
                record.Outcome = StepOutcome.RolledBack;
        }

        state.Status      = WorkflowStatus.RolledBack;
        state.StatusText  = rolledBackSteps.Count > 0
            ? $"Rolled back: {string.Join(", ", rolledBackSteps)}"
            : "No steps required rollback.";
        state.CompletedAt = DateTimeOffset.Now;

        Report(progress, state);
        return state;
    }

    private static void Report(
        IProgress<WorkflowExecutionState>? progress,
        WorkflowExecutionState             state)
    {
        progress?.Report(state);
    }
}
