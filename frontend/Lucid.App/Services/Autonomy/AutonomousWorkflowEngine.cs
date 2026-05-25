using Lucid.Services.Timeline;

namespace Lucid.Services.Autonomy;

/// <summary>
/// Top-level orchestrator for autonomous workflow execution (Phase 17F).
///
/// Pipeline for each user request:
///   1. Resolve the operational goal (keyword matching, not an LLM)
///   2. Plan a complete, inspectable workflow
///   3. Execute steps with safety validation, human review gates, and checkpointing
///   4. Narrate every phase to the companion overlay
///
/// Only one workflow runs at a time. The engine is interruptible at any step.
/// Never auto-executes anything — every file-modifying step requires explicit user approval.
///
/// Threading: WorkflowPlanned, WorkflowProgress, and WorkflowCompleted are raised
/// on the thread pool. Callers must marshal to the UI thread as needed.
/// </summary>
public sealed class AutonomousWorkflowEngine
{
    private readonly OperationalGoalResolver    _goalResolver;
    private readonly WorkflowExecutionPlanner   _planner;
    private readonly TaskCompletionCoordinator  _coordinator;
    private readonly ExecutionNarrativeService  _narrative;
    private readonly WorkflowCheckpointManager  _checkpoints;
    private readonly ITimelineAggregationService _timeline;

    private AutonomousWorkflow?      _activeWorkflow;
    private CancellationTokenSource? _cts;
    private readonly object          _lock = new();

    public AutonomousWorkflowEngine(
        OperationalGoalResolver    goalResolver,
        WorkflowExecutionPlanner   planner,
        TaskCompletionCoordinator  coordinator,
        ExecutionNarrativeService  narrative,
        WorkflowCheckpointManager  checkpoints,
        ITimelineAggregationService timeline)
    {
        _goalResolver = goalResolver;
        _planner      = planner;
        _coordinator  = coordinator;
        _narrative    = narrative;
        _checkpoints  = checkpoints;
        _timeline     = timeline;

        _coordinator.StepProgress += OnStepProgress;
    }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised when a workflow is planned and ready to display.</summary>
    public event EventHandler<AutonomousWorkflow>? WorkflowPlanned;

    /// <summary>Raised on each step transition during execution.</summary>
    public event EventHandler<WorkflowProgressEventArgs>? WorkflowProgress;

    /// <summary>Raised when a workflow finishes, is cancelled, or fails.</summary>
    public event EventHandler<WorkflowCompletedEventArgs>? WorkflowCompleted;

    /// <summary>Raised when a request cannot be mapped to a supported goal.</summary>
    public event EventHandler<string>? GoalUnresolved;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the request to a goal, plans the workflow, and executes it.
    /// Returns the final <see cref="WorkflowLifecycleStatus"/>.
    ///
    /// If a workflow is already running, raises <see cref="GoalUnresolved"/> and returns Failed.
    /// </summary>
    public async Task<WorkflowLifecycleStatus> ExecuteRequestAsync(
        string            request,
        CancellationToken externalCt = default)
    {
        // ── Guard: only one workflow at a time ────────────────────────────────
        lock (_lock)
        {
            if (_activeWorkflow?.Status == WorkflowLifecycleStatus.Running)
            {
                GoalUnresolved?.Invoke(this,
                    "A workflow is already running. Cancel or wait for it to complete first.");
                return WorkflowLifecycleStatus.Failed;
            }
        }

        // ── Resolve goal ──────────────────────────────────────────────────────
        var goal = _goalResolver.Resolve(request);

        if (goal.Type == OperationalGoalType.Unknown || goal.Confidence < 60)
        {
            GoalUnresolved?.Invoke(this, _narrative.NarrateGoalUnresolved(request));
            return WorkflowLifecycleStatus.Failed;
        }

        // ── Plan workflow ─────────────────────────────────────────────────────
        var workflow = _planner.PlanWorkflow(goal);

        lock (_lock) { _activeWorkflow = workflow; }

        _timeline.AddExternalEvent(new TimelineEvent
        {
            Id         = TimelineEvent.NewId(),
            Type       = TimelineEventType.AutonomousWorkflowPlanned,
            OccurredAt = DateTimeOffset.Now,
            Title      = $"Workflow planned: {workflow.Title}",
            Detail     = $"{workflow.Steps.Count} steps · ~{FormatDuration(workflow.EstimatedSeconds)}",
            Severity   = TimelineEventSeverity.Info,
            WorkflowId = workflow.Id,
        });

        WorkflowPlanned?.Invoke(this, workflow);

        // ── Execute ───────────────────────────────────────────────────────────
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        lock (_lock) { _cts = cts; }

        WorkflowLifecycleStatus finalStatus;
        try
        {
            finalStatus = await _coordinator.RunAsync(workflow, cts.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            lock (_lock) { _cts = null; }
        }

        // ── Record completion to timeline ─────────────────────────────────────
        var timelineType = finalStatus switch
        {
            WorkflowLifecycleStatus.Completed  => TimelineEventType.AutonomousWorkflowCompleted,
            WorkflowLifecycleStatus.RolledBack => TimelineEventType.AutonomousWorkflowRollback,
            _                                  => TimelineEventType.AutomationCancelled,
        };

        _timeline.AddExternalEvent(new TimelineEvent
        {
            Id         = TimelineEvent.NewId(),
            Type       = timelineType,
            OccurredAt = DateTimeOffset.Now,
            Title      = $"{FinalStatusLabel(finalStatus)}: {workflow.Title}",
            Severity   = finalStatus == WorkflowLifecycleStatus.Completed
                             ? TimelineEventSeverity.Good : TimelineEventSeverity.Info,
            WorkflowId = workflow.Id,
        });

        WorkflowCompleted?.Invoke(this, new WorkflowCompletedEventArgs
        {
            WorkflowId    = workflow.Id,
            FinalStatus   = finalStatus,
            Summary       = SummaryFor(workflow, finalStatus),
            FailureReason = workflow.FailureReason,
        });

        lock (_lock)
        {
            if (_activeWorkflow?.Id == workflow.Id)
                _activeWorkflow = null;
        }

        return finalStatus;
    }

    /// <summary>
    /// Cancels the currently running workflow. Returns false if nothing is running.
    /// </summary>
    public bool CancelCurrentWorkflow()
    {
        lock (_lock)
        {
            if (_cts is null) return false;
            _cts.Cancel();
            return true;
        }
    }

    /// <summary>Returns the active workflow, or null if none is running.</summary>
    public AutonomousWorkflow? GetActiveWorkflow()
    {
        lock (_lock) { return _activeWorkflow; }
    }

    /// <summary>True if a workflow is currently executing.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _activeWorkflow?.Status == WorkflowLifecycleStatus.Running;
            }
        }
    }

    // ── Event relay ───────────────────────────────────────────────────────────

    private void OnStepProgress(object? sender, WorkflowProgressEventArgs args)
        => WorkflowProgress?.Invoke(this, args);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FinalStatusLabel(WorkflowLifecycleStatus status) => status switch
    {
        WorkflowLifecycleStatus.Completed  => "Completed",
        WorkflowLifecycleStatus.Cancelled  => "Cancelled",
        WorkflowLifecycleStatus.Failed     => "Failed",
        WorkflowLifecycleStatus.RolledBack => "Rolled back",
        _                                  => status.ToString(),
    };

    private string SummaryFor(AutonomousWorkflow workflow, WorkflowLifecycleStatus status) =>
        status switch
        {
            WorkflowLifecycleStatus.Completed  => _narrative.NarrateWorkflowCompleted(workflow),
            WorkflowLifecycleStatus.Cancelled  =>
                _narrative.NarrateWorkflowCancelled(workflow.CompletedStepIds.Count),
            WorkflowLifecycleStatus.Failed     =>
                _narrative.NarrateWorkflowFailed(workflow, workflow.FailureReason),
            _                                  => $"{workflow.Title}: {status}.",
        };

    private static string FormatDuration(int seconds) => seconds switch
    {
        < 60   => $"{seconds}s",
        < 3600 => $"{seconds / 60}m",
        _      => $"{seconds / 3600}h",
    };
}
