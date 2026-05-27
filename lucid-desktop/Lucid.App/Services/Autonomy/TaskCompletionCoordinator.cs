using Lucid.Services.Execution;
using Lucid.Services.Timeline;

namespace Lucid.Services.Autonomy;

/// <summary>
/// Step sequencer for <see cref="AutonomousWorkflowEngine"/>.
///
/// Executes each workflow step in order, applying safety validation, human review
/// gates, and checkpointing between steps. Handles step-level failures without
/// crashing the engine.
///
/// Threading: RunAsync runs on the thread pool. StepProgress is raised on the
/// calling thread — the engine marshals to the UI thread before raising its own events.
/// </summary>
public sealed class TaskCompletionCoordinator
{
    private readonly SafeExecutionValidator          _validator;
    private readonly HumanReviewGate                 _reviewGate;
    private readonly WorkflowCheckpointManager       _checkpoints;
    private readonly ExecutionNarrativeService       _narrative;
    private readonly FileOrganizationWorkflowService _fileOrg;
    private readonly OperationalFileDiscoveryService _discovery;
    private readonly IActionExecutionEngine          _executionEngine;
    private readonly ITimelineAggregationService     _timeline;

    // Per-workflow pending previews (workflow ID → preview built in the Preview step)
    private readonly Dictionary<string, FileOrganizationPreview> _pendingPreviews = [];

    public TaskCompletionCoordinator(
        SafeExecutionValidator          validator,
        HumanReviewGate                 reviewGate,
        WorkflowCheckpointManager       checkpoints,
        ExecutionNarrativeService       narrative,
        FileOrganizationWorkflowService fileOrg,
        OperationalFileDiscoveryService discovery,
        IActionExecutionEngine          executionEngine,
        ITimelineAggregationService     timeline)
    {
        _validator       = validator;
        _reviewGate      = reviewGate;
        _checkpoints     = checkpoints;
        _narrative       = narrative;
        _fileOrg         = fileOrg;
        _discovery       = discovery;
        _executionEngine = executionEngine;
        _timeline        = timeline;
    }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised after each step starts or completes to drive UI progress display.</summary>
    public event EventHandler<WorkflowProgressEventArgs>? StepProgress;

    // ── Execution ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a workflow from its current <see cref="AutonomousWorkflow.CurrentStepIndex"/>
    /// through to completion or until cancelled/denied.
    ///
    /// Returns the final <see cref="WorkflowLifecycleStatus"/>.
    /// </summary>
    public async Task<WorkflowLifecycleStatus> RunAsync(
        AutonomousWorkflow workflow,
        CancellationToken  ct = default)
    {
        workflow.Status    = WorkflowLifecycleStatus.Running;
        workflow.StartedAt ??= DateTimeOffset.Now;

        var steps = workflow.Steps;

        for (int i = workflow.CurrentStepIndex; i < steps.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                workflow.Status = WorkflowLifecycleStatus.Cancelled;
                return workflow.Status;
            }

            var step = steps[i];
            workflow.CurrentStepIndex = i;

            // ── Safety validation ─────────────────────────────────────────────
            var blockReason = _validator.ValidateStep(step);
            if (blockReason is not null)
            {
                RaiseProgress(workflow, i, steps.Count, step.Title,
                    _narrative.NarrateValidationBlocked(blockReason), isFailed: true);
                workflow.Status        = WorkflowLifecycleStatus.Failed;
                workflow.FailureReason = blockReason;
                return workflow.Status;
            }

            // ── Narrate step start ────────────────────────────────────────────
            var startNarration = step.StepType switch
            {
                WorkflowStepType.Discovery        => _narrative.NarrateDiscoveryStarted(step),
                WorkflowStepType.Analysis         => _narrative.NarrateAnalysisStarted(step),
                WorkflowStepType.Preview          => _narrative.NarratePreviewBuilding(),
                WorkflowStepType.RequiresApproval => _narrative.NarrateReviewRequired(step),
                WorkflowStepType.FileOperation or
                WorkflowStepType.SystemOperation  => _narrative.NarrateApproved(step),
                _ => $"Running: {step.Title}…",
            };

            RaiseProgress(workflow, i, steps.Count, step.Title, startNarration);

            // ── Execute step ──────────────────────────────────────────────────
            try
            {
                var success = await ExecuteStepAsync(workflow, step, i, ct)
                    .ConfigureAwait(false);

                if (!success)
                {
                    workflow.Status = WorkflowLifecycleStatus.Cancelled;
                    return workflow.Status;
                }
            }
            catch (OperationCanceledException)
            {
                workflow.Status = WorkflowLifecycleStatus.Cancelled;
                return workflow.Status;
            }
            catch (Exception ex)
            {
                var reason = ex.Message;
                RaiseProgress(workflow, i, steps.Count, step.Title,
                    _narrative.NarrateStepFailed(step, reason), isFailed: true);
                workflow.Status        = WorkflowLifecycleStatus.Failed;
                workflow.FailureReason = reason;
                return workflow.Status;
            }

            // ── Mark step complete ────────────────────────────────────────────
            workflow.CompletedStepIds.Add(step.Id);
            RaiseProgress(workflow, i + 1, steps.Count, step.Title,
                _narrative.NarrateStepCompleted(step));

            // ── Save checkpoint ───────────────────────────────────────────────
            _checkpoints.SaveCheckpoint(workflow,
                _narrative.NarrateCheckpointSaved(i + 1, steps.Count));
        }

        // ── Workflow complete ─────────────────────────────────────────────────
        workflow.Status      = WorkflowLifecycleStatus.Completed;
        workflow.CompletedAt = DateTimeOffset.Now;

        _checkpoints.ClearCheckpoint(workflow.Id);
        _pendingPreviews.Remove(workflow.Id);

        RaiseProgress(workflow, steps.Count, steps.Count,
            "Complete", _narrative.NarrateWorkflowCompleted(workflow), isCompleted: true);

        return workflow.Status;
    }

    // ── Step dispatch ─────────────────────────────────────────────────────────

    /// <summary>Returns true if the step succeeded; false if denied (workflow stops).</summary>
    private async Task<bool> ExecuteStepAsync(
        AutonomousWorkflow workflow,
        WorkflowStep       step,
        int                stepIndex,
        CancellationToken  ct)
    {
        switch (step.StepType)
        {
            case WorkflowStepType.Discovery:
            case WorkflowStepType.Analysis:
            {
                // Read-only scans — always succeed
                if (!string.IsNullOrWhiteSpace(step.TargetPath))
                {
                    await _discovery.DiscoverAsync(workflow.Goal.Type, step.TargetPath, ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(400, ct).ConfigureAwait(false);
                }
                return true;
            }

            case WorkflowStepType.Preview:
            {
                // Build the file organization preview and cache it
                var targetPath = step.TargetPath
                    ?? workflow.Steps.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.TargetPath))?.TargetPath;

                if (!string.IsNullOrWhiteSpace(targetPath))
                {
                    var preview = await _fileOrg.BuildOrganizationPreviewAsync(
                        targetPath, workflow.Goal.Type, ct).ConfigureAwait(false);

                    _pendingPreviews[workflow.Id] = preview;

                    RaiseProgress(workflow, stepIndex, workflow.Steps.Count,
                        step.Title, _narrative.NarratePreviewReady(preview));
                }
                return true;
            }

            case WorkflowStepType.RequiresApproval:
            {
                // Show review gate and wait for user decision
                _pendingPreviews.TryGetValue(workflow.Id, out var preview);
                var narration = _narrative.NarrateReviewRequired(step);

                var approved = await _reviewGate.RequestReviewAsync(
                    workflow, step, preview, narration, ct).ConfigureAwait(false);

                if (!approved)
                {
                    RaiseProgress(workflow, stepIndex, workflow.Steps.Count,
                        step.Title, _narrative.NarrateDenied(step));
                    return false;
                }

                if (preview is not null)
                    preview.IsApproved = true;

                return true;
            }

            case WorkflowStepType.FileOperation:
            {
                if (_pendingPreviews.TryGetValue(workflow.Id, out var preview) &&
                    preview.IsApproved)
                {
                    await _fileOrg.ExecutePreviewAsync(preview, ct).ConfigureAwait(false);
                    _pendingPreviews.Remove(workflow.Id);
                }
                else if (!string.IsNullOrWhiteSpace(step.ActionId) &&
                         _executionEngine.IsSupported(step.ActionId))
                {
                    var ctx    = new ActionExecutionContext { ConfirmationGranted = true, RequestedBy = "autonomy" };
                    var result = await _executionEngine.ExecuteAsync(step.ActionId!, ctx, ct)
                        .ConfigureAwait(false);
                    if (result.Status == ActionExecutionStatus.Failed)
                        throw new InvalidOperationException(result.ErrorDetail ?? "File operation failed.");
                }
                return true;
            }

            case WorkflowStepType.SystemOperation:
            {
                if (!string.IsNullOrWhiteSpace(step.ActionId) &&
                    _executionEngine.IsSupported(step.ActionId))
                {
                    var ctx    = new ActionExecutionContext { ConfirmationGranted = true, RequestedBy = "autonomy" };
                    var result = await _executionEngine.ExecuteAsync(step.ActionId!, ctx, ct)
                        .ConfigureAwait(false);
                    if (result.Status == ActionExecutionStatus.Failed)
                        throw new InvalidOperationException(result.ErrorDetail ?? "System operation failed.");
                }
                return true;
            }

            case WorkflowStepType.Navigation:
            {
                // Open a system tool — fire and don't await failures
                if (!string.IsNullOrWhiteSpace(step.ActionId) &&
                    _executionEngine.IsSupported(step.ActionId))
                {
                    var ctx = new ActionExecutionContext { ConfirmationGranted = true, RequestedBy = "autonomy" };
                    _ = _executionEngine.ExecuteAsync(step.ActionId!, ctx, CancellationToken.None);
                }
                return true;
            }

            case WorkflowStepType.Narration:
            default:
                // Narration — no execution, brief pause for readability
                await Task.Delay(200, ct).ConfigureAwait(false);
                return true;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RaiseProgress(
        AutonomousWorkflow workflow,
        int                currentStep,
        int                totalSteps,
        string             stepTitle,
        string             narration,
        bool               isCompleted = false,
        bool               isFailed    = false)
    {
        StepProgress?.Invoke(this, new WorkflowProgressEventArgs
        {
            WorkflowId  = workflow.Id,
            CurrentStep = currentStep,
            TotalSteps  = totalSteps,
            StepTitle   = stepTitle,
            Narration   = narration,
            IsCompleted = isCompleted,
            IsFailed    = isFailed,
        });
    }
}
