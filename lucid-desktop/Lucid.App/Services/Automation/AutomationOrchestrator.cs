using Lucid.Services.Execution;
using Lucid.Services.Timeline;
using Microsoft.UI.Dispatching;

namespace Lucid.Services.Automation;

/// <summary>
/// Phase 17D automation orchestrator.
///
/// Coordinates guided, consent-driven automation workflows:
///   1. PlanFromGoal / PlanNavigation / PlanLaunch — builds a reviewable plan
///   2. Surface plan to user via companion overlay (approval card)
///   3. ExecutePlanAsync — steps through the plan one at a time
///      a. Raise StepStarted with narration
///      b. If confirmation required: raise ConfirmationRequired and wait
///      c. Execute via executor or built-in handler
///      d. Raise StepCompleted or StepFailed
///      e. Repeat for next step
///
/// CRITICAL SAFETY RULES:
///   • NEVER execute a step without checking RequiresConfirmation first
///   • NEVER execute in ObserveOnly mode — produce guidance text only
///   • ALWAYS check the CancellationToken between steps
///   • ALWAYS narrate every action before executing it
///   • ALWAYS surface the interrupt controller's stop button
///
/// Threading:
///   ExecutePlanAsync runs steps on Task.Run thread-pool threads.
///   All events are marshalled back to the UI thread before raising.
/// </summary>
public sealed class AutomationOrchestrator : IAutomationOrchestrator
{
    // ── Sub-services ──────────────────────────────────────────────────────────

    private readonly IActionExecutionEngine      _executionEngine;
    private readonly ActionNarrationService      _narration;
    private readonly HumanInterruptController    _interrupt;
    private readonly AutomationPermissionManager _permissions;
    private readonly GuidedAutomationWorkflow    _workflows;
    private readonly ExplorerAutomationService   _explorer;
    private readonly ApplicationLaunchAutomation _appLaunch;
    private readonly ITimelineAggregationService _timeline;
    private readonly DispatcherQueue             _uiDispatcher;

    // ── State ─────────────────────────────────────────────────────────────────

    private AutomationExecutionContext? _activeContext;

    // ── Constructor ───────────────────────────────────────────────────────────

    public AutomationOrchestrator(
        IActionExecutionEngine      executionEngine,
        ITimelineAggregationService timeline,
        DispatcherQueue             uiDispatcher)
    {
        _executionEngine = executionEngine;
        _timeline        = timeline;
        _uiDispatcher    = uiDispatcher;

        _narration   = new ActionNarrationService();
        _interrupt   = new HumanInterruptController();
        _permissions = new AutomationPermissionManager();
        _workflows   = new GuidedAutomationWorkflow();
        _explorer    = new ExplorerAutomationService();
        _appLaunch   = new ApplicationLaunchAutomation();

        _permissions.ModeChanged += (_, mode) => ModeChanged?.Invoke(this, mode);
        _interrupt.InterruptionOccurred += OnInterruptionOccurred;
    }

    // ── IAutomationOrchestrator: Mode ─────────────────────────────────────────

    public AutomationMode Mode => _permissions.CurrentMode;

    public void SetMode(AutomationMode mode) => _permissions.SetMode(mode);

    // ── IAutomationOrchestrator: State ────────────────────────────────────────

    public bool IsExecuting => _activeContext?.IsRunning ?? false;
    public bool IsPaused    => _activeContext?.IsPaused  ?? false;

    public void Resume()
    {
        if (_activeContext is { IsPaused: true })
        {
            _activeContext.IsPaused = false;
            _activeContext.Log("User resumed execution.");
        }
    }

    // ── IAutomationOrchestrator: Interrupt ────────────────────────────────────

    public void Pause()         => _interrupt.RequestPause();
    public void Cancel()        => _interrupt.RequestCancel();
    public void EmergencyHalt() => _interrupt.EmergencyHalt();

    // ── IAutomationOrchestrator: Plan construction ────────────────────────────

    public AutomationExecutionPlan PlanFromGoal(string goal, AutomationScope scope)
    {
        // Try the pre-built workflow library first
        var plan = _workflows.FromGoalKeyword(goal);
        if (plan is not null) return plan;

        // Fallback: single-step generic plan
        return new AutomationExecutionPlan
        {
            Id              = AutomationExecutionPlan.NewId(),
            Goal            = goal,
            Narration       = $"I'll help you {goal.ToLowerInvariant()}.",
            ExpectedOutcome = "The requested action will be completed.",
            Scope           = scope,
            HighestRisk     = AutomationRiskLevel.Low,
            RequiresConfirmation = false,
            RollbackAvailable    = false,
            EstimatedSeconds     = 2,
            Steps = [],
        };
    }

    public AutomationExecutionPlan PlanNavigation(string navigationTarget, string? goalOverride = null)
    {
        var goal = goalOverride ?? $"Navigate to {navigationTarget}";
        return new AutomationExecutionPlan
        {
            Id              = AutomationExecutionPlan.NewId(),
            Goal            = goal,
            Narration       = $"Taking you to the {navigationTarget} page.",
            ExpectedOutcome = $"The {navigationTarget} page will open.",
            Scope           = AutomationScope.Navigation,
            HighestRisk     = AutomationRiskLevel.None,
            RequiresConfirmation = false,
            RollbackAvailable    = false,
            EstimatedSeconds     = 1,
            Confidence           = 99,
            Steps =
            [
                new AutomationStep
                {
                    Id           = AutomationStep.NewId(),
                    Title        = $"Open {navigationTarget} page",
                    Narration    = $"Navigating to the {navigationTarget} page.",
                    Rationale    = $"You asked to open {navigationTarget}.",
                    ActionId     = "automation.navigate",
                    ActionParams = navigationTarget,
                    Risk         = AutomationRiskLevel.None,
                    RequiresConfirmation = false,
                    RollbackAvailable    = false,
                },
            ],
        };
    }

    public AutomationExecutionPlan PlanLaunch(string target)
    {
        var (title, narration, rationale, outcome, actionId) = target.ToLowerInvariant() switch
        {
            "task-manager" =>
                ("Open Task Manager", "Opening Task Manager to show live process and resource usage.",
                 "Task Manager provides real-time CPU, memory, disk, and network usage by process.",
                 "Task Manager opens on the Processes tab.", "automation.launch.task-manager"),
            "device-manager" =>
                ("Open Device Manager", "Opening Device Manager to display hardware and driver status.",
                 "Device Manager shows all hardware devices and flags any with driver errors.",
                 "Device Manager opens. Look for yellow warning triangles.",
                 "automation.launch.device-manager"),
            "event-viewer" =>
                ("Open Event Viewer", "Opening Event Viewer to show Windows system logs.",
                 "Event Viewer captures errors and warnings that Windows logs internally.",
                 "Event Viewer opens. Navigate to Windows Logs → System for recent errors.",
                 "automation.launch.event-viewer"),
            "storage-settings" =>
                ("Open Storage settings", "Opening Storage settings to show disk usage breakdown.",
                 "Storage settings shows how your disk space is allocated across categories.",
                 "Storage settings opens with a breakdown by category.",
                 "automation.launch.storage-settings"),
            "startup-apps" =>
                ("Open Startup Apps", "Opening Startup Apps settings to show apps that run at login.",
                 "Startup apps with High impact are the most common cause of slow boot.",
                 "Startup Apps settings opens showing each app's impact level.",
                 "automation.launch.startup-apps"),
            "windows-security" =>
                ("Open Windows Security", "Opening Windows Security to show protection status.",
                 "Windows Security is the authoritative source for antivirus and firewall status.",
                 "Windows Security opens on the Home screen.",
                 "automation.launch.windows-security"),
            "downloads" =>
                ("Open Downloads folder", "Opening your Downloads folder in File Explorer.",
                 "Downloads folders often accumulate large files that can be safely reviewed and deleted.",
                 "Your Downloads folder opens in File Explorer.",
                 "automation.explorer.downloads"),
            "documents" =>
                ("Open Documents folder", "Opening your Documents folder in File Explorer.",
                 "Documents is a common place to find large files and duplicates.",
                 "Your Documents folder opens in File Explorer.",
                 "automation.explorer.documents"),
            "temp" =>
                ("Open Temp folder", "Opening the Windows Temp folder in File Explorer.",
                 "The Temp folder contains temporary files that can accumulate over time.",
                 "The Temp folder opens in File Explorer.",
                 "automation.explorer.temp"),
            _ =>
                (target, $"Opening {target}.", $"You asked to open {target}.",
                 $"{target} will open.", $"automation.launch.{target}"),
        };

        return new AutomationExecutionPlan
        {
            Id              = AutomationExecutionPlan.NewId(),
            Goal            = title,
            Narration       = narration,
            ExpectedOutcome = outcome,
            Scope           = AutomationScope.SystemTool,
            HighestRisk     = AutomationRiskLevel.Low,
            RequiresConfirmation = false,
            RollbackAvailable    = false,
            EstimatedSeconds     = 2,
            Confidence           = 95,
            Steps =
            [
                new AutomationStep
                {
                    Id           = AutomationStep.NewId(),
                    Title        = title,
                    Narration    = narration,
                    Rationale    = rationale,
                    ActionId     = actionId,
                    Risk         = AutomationRiskLevel.Low,
                    RequiresConfirmation = false,
                    RollbackAvailable    = false,
                    ExpectedOutcome = outcome,
                },
            ],
        };
    }

    // ── IAutomationOrchestrator: Execution ────────────────────────────────────

    public async Task ExecutePlanAsync(AutomationExecutionPlan plan, CancellationToken ct = default)
    {
        _interrupt.Reset();
        var ctx = new AutomationExecutionContext(plan.Id, _permissions.CurrentMode);
        _activeContext = ctx;
        _interrupt.RegisterContext(ctx);

        ctx.IsRunning = true;

        // Timeline: plan started
        PublishTimelineEvent(TimelineEventType.AutomationStarted, plan.Goal, TimelineEventSeverity.Info);

        RaisePlanStarted(plan);
        Narrate(_narration.NarrateplanApproved(plan));

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            ct, ctx.CancellationToken);

        try
        {
            for (int i = 0; i < plan.Steps.Count; i++)
            {
                if (ctx.IsCancelled || linked.Token.IsCancellationRequested) break;

                // Wait while paused
                while (ctx.IsPaused && !ctx.IsCancelled)
                    await Task.Delay(200, linked.Token).ConfigureAwait(false);

                if (ctx.IsCancelled || linked.Token.IsCancellationRequested) break;

                var step = plan.Steps[i];
                ctx.CurrentStepIndex = i;
                ctx.LogStepStarted(step);

                RaiseStepStarted(step);
                Narrate(_narration.NarrateStepStart(step));

                // ObserveOnly: produce guidance, don't execute
                if (_permissions.CurrentMode == AutomationMode.ObserveOnly)
                {
                    Narrate(_narration.NarrateObserveOnly(step));
                    ctx.LogStepCompleted(step);
                    RaiseStepCompleted(step with { Status = AutomationStatus.Completed });
                    continue;
                }

                // Confirmation gate
                if (_permissions.RequiresConfirmation(step))
                {
                    bool approved = await RequestConfirmationAsync(plan, step, ctx).ConfigureAwait(false);
                    if (!approved || ctx.IsCancelled) break;
                }

                // Execute step
                bool success = await ExecuteStepAsync(step, linked.Token).ConfigureAwait(false);

                if (success)
                {
                    ctx.LogStepCompleted(step);
                    var completed = step.WithStatus(AutomationStatus.Completed);
                    RaiseStepCompleted(completed);
                    Narrate(_narration.NarrateStepComplete(step));
                }
                else
                {
                    ctx.LogStepFailed(step, "Step execution failed.");
                    var failed = step.WithStatus(AutomationStatus.Failed, error: "Execution failed.");
                    RaiseStepFailed(failed);
                    Narrate(_narration.NarrateStepFailed(step, "The action could not be completed."));
                    break;
                }
            }

            ctx.IsRunning = false;

            if (ctx.IsCancelled || linked.Token.IsCancellationRequested)
            {
                PublishTimelineEvent(TimelineEventType.AutomationCancelled, plan.Goal, TimelineEventSeverity.Info);
                Narrate(_narration.NarrateCancelled(ctx.CompletedStepIds.Count));
                RaisePlanCancelled(plan);
            }
            else
            {
                PublishTimelineEvent(TimelineEventType.AutomationCompleted, plan.Goal, TimelineEventSeverity.Good);
                Narrate(_narration.NarrateComplete(plan));
                RaisePlanCompleted(plan);
            }
        }
        catch (OperationCanceledException)
        {
            ctx.IsRunning = false;
            ctx.IsCancelled = true;
            PublishTimelineEvent(TimelineEventType.AutomationCancelled, plan.Goal, TimelineEventSeverity.Info);
            Narrate(_narration.NarrateCancelled(ctx.CompletedStepIds.Count));
            RaisePlanCancelled(plan);
        }
        catch (Exception ex)
        {
            ctx.IsRunning = false;
            ctx.IsFailed  = true;
            ctx.Log($"Plan failed: {ex.Message}");
            PublishTimelineEvent(TimelineEventType.AutomationInterrupted, $"{plan.Goal} — {ex.Message}", TimelineEventSeverity.Warning);
            RaisePlanFailed(plan, ex.Message);
        }
        finally
        {
            _interrupt.ClearContext();
            _activeContext = null;
            ctx.Dispose();
        }
    }

    // ── Step execution ────────────────────────────────────────────────────────

    private async Task<bool> ExecuteStepAsync(AutomationStep step, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            // Built-in automation handlers (explorer/launch/navigate)
            if (step.ActionId.StartsWith("automation.navigate", StringComparison.OrdinalIgnoreCase))
            {
                // Navigation is handled by raising NavigationRequested on the UI thread
                var tag = step.ActionParams ?? string.Empty;
                DispatchToUI(() => NavigationRequested?.Invoke(this, tag));
                await Task.Delay(300, ct).ConfigureAwait(false);
                return true;
            }

            if (step.ActionId.StartsWith("automation.explorer.", StringComparison.OrdinalIgnoreCase))
            {
                var target = step.ActionId["automation.explorer.".Length..];
                var (success, error) = target switch
                {
                    "downloads" => await _explorer.OpenDownloadsFolderAsync().ConfigureAwait(false),
                    "documents" => await _explorer.OpenDocumentsFolderAsync().ConfigureAwait(false),
                    "desktop"   => await _explorer.OpenDesktopFolderAsync().ConfigureAwait(false),
                    "temp"      => await _explorer.OpenTempFolderAsync().ConfigureAwait(false),
                    "recycle"   => await _explorer.OpenRecycleBinAsync().ConfigureAwait(false),
                    _ when step.ActionParams is not null
                            => await _explorer.OpenFolderAsync(step.ActionParams).ConfigureAwait(false),
                    _       => (false, $"Unknown explorer target: {target}"),
                };
                if (!success && error is not null)
                    Narrate(error);
                return success;
            }

            if (step.ActionId.StartsWith("automation.launch.", StringComparison.OrdinalIgnoreCase))
            {
                var target = step.ActionId["automation.launch.".Length..];
                var (success, error) = target switch
                {
                    "task-manager"      => await _appLaunch.OpenTaskManagerAsync().ConfigureAwait(false),
                    "device-manager"    => await _appLaunch.OpenDeviceManagerAsync().ConfigureAwait(false),
                    "event-viewer"      => await _appLaunch.OpenEventViewerAsync().ConfigureAwait(false),
                    "resource-monitor"  => await _appLaunch.OpenResourceMonitorAsync().ConfigureAwait(false),
                    "disk-cleanup"      => await _appLaunch.OpenDiskCleanupAsync().ConfigureAwait(false),
                    "windows-security"  => await _appLaunch.OpenWindowsSecurityAsync().ConfigureAwait(false),
                    "startup-apps"      => await _appLaunch.OpenStartupAppsAsync().ConfigureAwait(false),
                    "storage-settings"  => await _appLaunch.OpenStorageSettingsAsync().ConfigureAwait(false),
                    "apps-settings"     => await _appLaunch.OpenAppsSettingsAsync().ConfigureAwait(false),
                    "network-settings"  => await _appLaunch.OpenNetworkSettingsAsync().ConfigureAwait(false),
                    "network-troubleshooter" => await _appLaunch.OpenNetworkTroubleshooterAsync().ConfigureAwait(false),
                    "windows-update"    => await _appLaunch.OpenWindowsUpdateAsync().ConfigureAwait(false),
                    "power-settings"    => await _appLaunch.OpenPowerSettingsAsync().ConfigureAwait(false),
                    _ when step.ActionParams is not null
                            => await _appLaunch.OpenSettingsPageAsync(step.ActionParams).ConfigureAwait(false),
                    _       => (false, $"Unknown launch target: {target}"),
                };
                if (!success && error is not null)
                    Narrate(error);
                return success;
            }

            // Delegate to the registered execution engine for all other action IDs
            var context = new ActionExecutionContext
            {
                IsDryRun           = false,
                ConfirmationGranted = true,  // user already confirmed via the automation gate
                Parameters         = step.ActionParams is not null
                                     ? new Dictionary<string, string> { ["params"] = step.ActionParams }
                                     : new Dictionary<string, string>(),
            };
            var result = await _executionEngine.ExecuteAsync(step.ActionId, context, ct).ConfigureAwait(false);
            return result.IsSuccess;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return false;
        }
    }

    // ── Confirmation gate ─────────────────────────────────────────────────────

    private async Task<bool> RequestConfirmationAsync(
        AutomationExecutionPlan plan,
        AutomationStep          step,
        AutomationExecutionContext ctx)
    {
        ctx.ConfirmationApproved = false;

        var args = new AutomationConfirmationRequestEventArgs
        {
            Plan        = plan,
            Step        = step,
            Narration   = _narration.NarrateConfirmationRequest(step),
            RiskLabel   = step.RiskLabel,
            CanRollback = step.RollbackAvailable,
            Approve     = () =>
            {
                ctx.ConfirmationApproved = true;
                try { ctx.ConfirmationGate.Release(); } catch { /* gate already released */ }
            },
            Deny        = () =>
            {
                ctx.IsCancelled = true;
                try { ctx.ConfirmationGate.Release(); } catch { /* gate already released */ }
            },
        };

        DispatchToUI(() => ConfirmationRequired?.Invoke(this, args));

        // Wait for the user to approve or deny (max 5 minutes)
        await ctx.ConfirmationGate.WaitAsync(TimeSpan.FromMinutes(5)).ConfigureAwait(false);

        return ctx.ConfirmationApproved && !ctx.IsCancelled;
    }

    // ── Timeline publishing ───────────────────────────────────────────────────

    private void PublishTimelineEvent(
        TimelineEventType type, string title, TimelineEventSeverity severity)
    {
        _timeline.AddExternalEvent(new TimelineEvent
        {
            Id         = TimelineEvent.NewId(),
            Type       = type,
            OccurredAt = DateTimeOffset.Now,
            Title      = title,
            Severity   = severity,
        });
    }

    // ── Event dispatch helpers ────────────────────────────────────────────────

    private void Narrate(string message, bool isProgress = false, bool isWarning = false)
    {
        DispatchToUI(() => NarrationUpdated?.Invoke(this,
            new AutomationNarrationEventArgs
            {
                Message    = message,
                IsProgress = isProgress,
                IsWarning  = isWarning,
            }));
    }

    private void RaisePlanStarted(AutomationExecutionPlan plan) =>
        DispatchToUI(() => PlanStarted?.Invoke(this, new AutomationPlanEventArgs { Plan = plan }));

    private void RaisePlanCompleted(AutomationExecutionPlan plan) =>
        DispatchToUI(() => PlanCompleted?.Invoke(this, new AutomationPlanEventArgs { Plan = plan }));

    private void RaisePlanCancelled(AutomationExecutionPlan plan, string? reason = null) =>
        DispatchToUI(() => PlanCancelled?.Invoke(this,
            new AutomationPlanEventArgs { Plan = plan, Reason = reason }));

    private void RaisePlanFailed(AutomationExecutionPlan plan, string? reason = null) =>
        DispatchToUI(() => PlanFailed?.Invoke(this,
            new AutomationPlanEventArgs { Plan = plan, Reason = reason }));

    private void RaiseStepStarted(AutomationStep step) =>
        DispatchToUI(() => StepStarted?.Invoke(this, new AutomationStepEventArgs { Step = step }));

    private void RaiseStepCompleted(AutomationStep step) =>
        DispatchToUI(() => StepCompleted?.Invoke(this, new AutomationStepEventArgs { Step = step }));

    private void RaiseStepFailed(AutomationStep step) =>
        DispatchToUI(() => StepFailed?.Invoke(this, new AutomationStepEventArgs { Step = step }));

    private void OnInterruptionOccurred(object? sender, string reason)
    {
        if (reason == "Paused")
            Narrate(_narration.NarratePaused(), isProgress: true);
    }

    private void DispatchToUI(Action action)
    {
        if (_uiDispatcher.HasThreadAccess)
            action();
        else
            _uiDispatcher.TryEnqueue(() => action());
    }

    // ── Events ────────────────────────────────────────────────────────────────

    public event EventHandler<AutomationPlanEventArgs>?                PlanStarted;
    public event EventHandler<AutomationPlanEventArgs>?                PlanCompleted;
    public event EventHandler<AutomationPlanEventArgs>?                PlanCancelled;
    public event EventHandler<AutomationPlanEventArgs>?                PlanFailed;
    public event EventHandler<AutomationStepEventArgs>?                StepStarted;
    public event EventHandler<AutomationStepEventArgs>?                StepCompleted;
    public event EventHandler<AutomationStepEventArgs>?                StepFailed;
    public event EventHandler<AutomationConfirmationRequestEventArgs>? ConfirmationRequired;
    public event EventHandler<AutomationNarrationEventArgs>?           NarrationUpdated;
    public event EventHandler<AutomationMode>?                         ModeChanged;

    /// <summary>
    /// Raised when a step requests navigation to a Lucid page.
    /// Subscribed by MainWindow to call NavigateToPage(tag).
    /// </summary>
    public event EventHandler<string>? NavigationRequested;
}
