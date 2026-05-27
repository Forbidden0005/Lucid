using Lucid.Services.Execution;
using Lucid.Services.Governance;
using Lucid.Services.Intelligence;
using Lucid.Services.Learning;
using Microsoft.UI.Dispatching;

namespace Lucid.Services.Remediation;

/// <summary>
/// Orchestrates autonomous remediation planning, supervised execution, and
/// post-remediation validation for the Lucid platform.
///
/// Design philosophy:
///   • Always suggests — never acts without explicit user approval.
///   • Trust level is the user's explicit declaration of how much autonomy
///     they grant the platform. It can only be raised by the user.
///   • Safety constraints are hard-wired and cannot be bypassed by trust level.
///   • All execution is transparent, logged, and rollback-aware.
///   • Fire-and-forget behavior is explicitly prohibited for destructive ops.
///
/// Trust model:
///   ManualOnly          — suggest only, never execute automatically
///   GuidedApproval      — user explicitly approves each workflow start
///   SupervisedAutomation — user approves start; each step executes unattended
///   RecoveryAutomation  — auto-rollback when stabilization worsens
/// </summary>
public sealed class AutonomousRemediationService : IDisposable
{
    private readonly IActionExecutionEngine    _engine;
    private readonly IRuntimeGovernanceService _governance;
    private readonly ISystemInsightEngine      _insights;
    private readonly IRemediationLearningService _learning;
    private readonly ITelemetryService         _telemetry;
    private readonly DispatcherQueue           _dispatcher;

    private readonly WorkflowExecutionCoordinator _coordinator;
    private readonly RemediationOutcomeValidator  _validator;

    // ── Active execution state ────────────────────────────────────────────────
    private WorkflowExecutionState?   _activeState;
    private CancellationTokenSource?  _activeCts;
    private Task?                     _activeTask;
    private readonly object           _lock = new();

    // ── Suggestions ───────────────────────────────────────────────────────────
    private IReadOnlyList<RemediationPlanSuggestion> _suggestions = [];
    private RemediationOutcomeReport?                _lastOutcome;

    // ── Trust level ───────────────────────────────────────────────────────────
    private AutomationTrustLevel _trustLevel = AutomationTrustLevel.GuidedApproval;

    public AutomationTrustLevel TrustLevel
    {
        get => _trustLevel;
        set
        {
            if (_trustLevel == value) return;
            _trustLevel = value;
            TrustLevelChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired on the UI thread when the suggestion list is refreshed.</summary>
    public event EventHandler<IReadOnlyList<RemediationPlanSuggestion>>? SuggestionsUpdated;

    /// <summary>Fired on the UI thread each time execution state changes.</summary>
    public event EventHandler<WorkflowExecutionState>? ExecutionUpdated;

    /// <summary>Fired on the UI thread when a workflow run completes (success, halt, cancel, or rollback).</summary>
    public event EventHandler<RemediationOutcomeReport>? ExecutionCompleted;

    /// <summary>Fired on the UI thread when the trust level changes.</summary>
    public event EventHandler? TrustLevelChanged;

    // ── State accessors ───────────────────────────────────────────────────────

    /// <summary>The currently running or most recently completed execution state. Null before first run.</summary>
    public WorkflowExecutionState? ActiveState => _activeState;

    /// <summary>True while a workflow execution is in progress.</summary>
    public bool IsExecuting
    {
        get
        {
            lock (_lock) return _activeTask is not null && !_activeTask.IsCompleted;
        }
    }

    /// <summary>The current suggestion list (may be empty before first refresh).</summary>
    public IReadOnlyList<RemediationPlanSuggestion> Suggestions => _suggestions;

    /// <summary>The outcome report from the most recently completed workflow. Null before first run.</summary>
    public RemediationOutcomeReport? LastOutcome => _lastOutcome;

    // ── Constructor ───────────────────────────────────────────────────────────

    public AutonomousRemediationService(
        IActionExecutionEngine    engine,
        IRuntimeGovernanceService governance,
        ISystemInsightEngine      insights,
        IRemediationLearningService learning,
        ITelemetryService         telemetry,
        DispatcherQueue           dispatcher)
    {
        _engine     = engine;
        _governance = governance;
        _insights   = insights;
        _learning   = learning;
        _telemetry  = telemetry;
        _dispatcher = dispatcher;

        _coordinator = new WorkflowExecutionCoordinator(_engine, _governance);
        _validator   = new RemediationOutcomeValidator(_learning);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Subscribes to insight changes so suggestions refresh automatically.
    /// Call once after construction.
    /// </summary>
    public void Start()
    {
        _insights.InsightsUpdated += OnInsightsUpdated;

        // Seed suggestions from the current insight set immediately.
        RefreshSuggestions(_insights.CurrentInsights);
    }

    /// <summary>
    /// Unsubscribes from events. Does not halt a running workflow — call
    /// HaltActiveWorkflowAsync() first if needed.
    /// </summary>
    public void Stop()
    {
        _insights.InsightsUpdated -= OnInsightsUpdated;
    }

    public void Dispose()
    {
        Stop();
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _activeCts = null;
    }

    // ── Suggestion refresh ────────────────────────────────────────────────────

    private void OnInsightsUpdated(object? sender, IReadOnlyList<SystemInsight> insights)
    {
        RefreshSuggestions(insights);
    }

    private void RefreshSuggestions(IReadOnlyList<SystemInsight> insights)
    {
        var suggestions = RemediationWorkflowPlanner.BuildSuggestions(insights);
        _suggestions = suggestions;
        FireOnUiThread(() => SuggestionsUpdated?.Invoke(this, suggestions));
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the full catalogue of available workflows regardless of current insights.
    /// </summary>
    public IReadOnlyList<RemediationWorkflow> GetAllWorkflows() =>
        RemediationWorkflowPlanner.AllWorkflows;

    /// <summary>
    /// Starts executing the specified workflow.
    ///
    /// Safety contract:
    ///   • The workflow must pass SafetyConstraintEngine.CheckPreConditions().
    ///   • A workflow can only run if no other workflow is currently running.
    ///   • RequiresConfirmation steps are skipped unless trust level >= SupervisedAutomation.
    ///
    /// Returns null if the workflow could not start, with a reason in the returned state.
    /// </summary>
    public async Task<WorkflowExecutionState?> StartWorkflowAsync(string workflowId)
    {
        // Find the workflow
        var workflow = RemediationWorkflowPlanner.AllWorkflows
            .FirstOrDefault(w => w.WorkflowId == workflowId);

        if (workflow is null)
        {
            return null;
        }

        lock (_lock)
        {
            if (IsExecuting) return null;
        }

        // Pre-conditions safety check
        var telemetry = _telemetry.LastReading;
        var preCheck  = SafetyConstraintEngine.CheckPreConditions(
            workflow, _governance.CurrentMode, telemetry, _trustLevel);

        if (preCheck.Status == SafetyCheckStatus.Blocked)
        {
            // Return a synthetic halted state so the UI can display the reason
            var blocked = new WorkflowExecutionState
            {
                Workflow     = workflow,
                WorkflowId   = workflow.WorkflowId,
                Status       = WorkflowStatus.Halted,
                HaltReason   = preCheck.Reason,
                StatusText   = $"Cannot start: {preCheck.Reason}",
                StartedAt    = DateTimeOffset.Now,
                CompletedAt  = DateTimeOffset.Now,
            };
            return blocked;
        }

        // Capture before snapshot
        var currentInsights  = _insights.CurrentInsights;
        var beforeSnapshot   = StabilizationVerificationEngine.CaptureSnapshot(telemetry, currentInsights);

        // Set up cancellation
        var cts = new CancellationTokenSource();
        lock (_lock) { _activeCts = cts; }

        // Build progress handler that marshals updates to UI thread
        var progress = new Progress<WorkflowExecutionState>(state =>
        {
            _activeState = state;
            FireOnUiThread(() => ExecutionUpdated?.Invoke(this, state));
        });

        // Execute on thread pool
        var task = Task.Run(async () =>
        {
            try
            {
                var finalState = await _coordinator.ExecuteAsync(
                    workflow,
                    beforeSnapshot,
                    _trustLevel,
                    progress,
                    cts.Token).ConfigureAwait(false);

                _activeState = finalState;

                // Post-execution: capture after snapshot and validate
                var afterTelemetry   = _telemetry.LastReading;
                var afterInsights    = _insights.CurrentInsights;
                var afterSnapshot    = StabilizationVerificationEngine.CaptureSnapshot(
                    afterTelemetry, afterInsights);

                finalState.AfterSnapshot = afterSnapshot;

                var outcome = _validator.Validate(finalState, afterInsights);
                _lastOutcome = outcome;

                FireOnUiThread(() =>
                {
                    ExecutionUpdated?.Invoke(this, finalState);
                    ExecutionCompleted?.Invoke(this, outcome);
                });
            }
            catch (Exception ex)
            {
                // Should never reach here (coordinator never throws), but just in case
                var errorState = _activeState ?? new WorkflowExecutionState
                {
                    Workflow    = workflow,
                    WorkflowId  = workflow.WorkflowId,
                    Status      = WorkflowStatus.Failed,
                    StatusText  = $"Unexpected error: {ex.Message}",
                    StartedAt   = DateTimeOffset.Now,
                    CompletedAt = DateTimeOffset.Now,
                };
                errorState.Status      = WorkflowStatus.Failed;
                errorState.StatusText  = $"Unexpected error: {ex.Message}";
                errorState.CompletedAt = DateTimeOffset.Now;
                _activeState           = errorState;

                var failedOutcome = _validator.Validate(errorState, _insights.CurrentInsights);
                _lastOutcome = failedOutcome;

                FireOnUiThread(() =>
                {
                    ExecutionUpdated?.Invoke(this, errorState);
                    ExecutionCompleted?.Invoke(this, failedOutcome);
                });
            }
            finally
            {
                lock (_lock)
                {
                    _activeTask = null;
                    cts.Dispose();
                    if (_activeCts == cts) _activeCts = null;
                }
            }
        }, cts.Token);

        lock (_lock) { _activeTask = task; }

        // Return current state (will be updated asynchronously via progress)
        return _activeState;
    }

    /// <summary>
    /// Requests cancellation of the currently running workflow.
    /// The execution coordinator handles the cancellation gracefully.
    /// </summary>
    public void HaltActiveWorkflow()
    {
        CancellationTokenSource? cts;
        lock (_lock) { cts = _activeCts; }
        cts?.Cancel();
    }

    /// <summary>
    /// Rolls back the most recently completed (or halted) workflow.
    /// Only available when the active state has rollback-capable steps.
    /// </summary>
    public async Task RollbackActiveWorkflowAsync()
    {
        if (_activeState is null) return;
        if (_activeState.Status is not (WorkflowStatus.Completed
                                     or WorkflowStatus.Halted
                                     or WorkflowStatus.Failed)) return;

        var progress = new Progress<WorkflowExecutionState>(state =>
        {
            _activeState = state;
            FireOnUiThread(() => ExecutionUpdated?.Invoke(this, state));
        });

        var rolledBack = await _coordinator.RollbackAsync(_activeState, progress).ConfigureAwait(false);
        _activeState = rolledBack;

        var outcome      = _validator.Validate(rolledBack, _insights.CurrentInsights);
        _lastOutcome     = outcome;

        FireOnUiThread(() =>
        {
            ExecutionUpdated?.Invoke(this, rolledBack);
            ExecutionCompleted?.Invoke(this, outcome);
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void FireOnUiThread(Action action)
    {
        if (_dispatcher.HasThreadAccess)
            action();
        else
            _dispatcher.TryEnqueue(() => action());
    }
}
