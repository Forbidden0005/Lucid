using System.Collections.ObjectModel;
using System.Security.Principal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Execution;
using Lucid.Services.History;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lucid.ViewModels;

// ── State enum ────────────────────────────────────────────────────────────────

/// <summary>
/// Lifecycle states of a single action execution attempt as seen by the UI.
///
/// Transitions:
///   Idle ──► ConfirmationPending ──► Running ──► Succeeded ──► RollingBack ──► RolledBack
///             │                  │              └──► PartiallySucceeded
///             │                  └──► (ConfirmationGranted=false) RequiresConfirmation
///             └──► (Cancel) Idle
///   Idle ──► DryRunning ──► DryRunComplete
///   Any ──► Failed
/// </summary>
public enum ActionExecutionState
{
    /// <summary>No execution in progress. Run and Preview controls are visible.</summary>
    Idle                = 0,

    /// <summary>
    /// Waiting for the user to confirm a risky action. The confirmation panel
    /// is shown; Run and Preview controls are hidden.
    /// </summary>
    ConfirmationPending = 1,

    /// <summary>Dry-run simulation is in progress.</summary>
    DryRunning          = 2,

    /// <summary>Dry-run completed. Log and result summary are shown.</summary>
    DryRunComplete      = 3,

    /// <summary>Live execution is in progress.</summary>
    Running             = 4,

    /// <summary>Execution completed successfully.</summary>
    Succeeded           = 5,

    /// <summary>Execution completed with partial results — some steps were skipped.</summary>
    PartiallySucceeded  = 6,

    /// <summary>Execution failed. Log and error summary are shown.</summary>
    Failed              = 7,

    /// <summary>A rollback is in progress.</summary>
    RollingBack         = 8,

    /// <summary>Rollback completed. Result summary reflects the rollback outcome.</summary>
    RolledBack          = 9,
}

// ── ViewModel ─────────────────────────────────────────────────────────────────

/// <summary>
/// Drives the execution UX for a single <see cref="SystemAction"/>.
///
/// Owns the full lifecycle from user intent (Run / Preview) through confirmation,
/// live execution logging, result display, and optional rollback.
///
/// Responsibilities:
///   • Translates user commands into <see cref="IActionExecutionEngine"/> calls.
///   • Streams log entries to <see cref="LogEntries"/> as they arrive so the
///     UI updates in real time during long-running future executors.
///   • Exposes all state as <see cref="Visibility"/> tokens and computed
///     properties so the DataTemplate requires no converters.
///   • Preserves user control: every state transition is explicit and reversible.
///
/// Relationship to other ViewModels:
///   Created and owned by <see cref="ActionCardViewModel"/> — one instance per
///   action card for the lifetime of the expanded insight card.
///
/// Thread safety:
///   Constructed on the UI thread. All public properties and commands must be
///   accessed from the UI thread. The live log callback marshals entries from
///   the executor thread back to the UI thread via <see cref="DispatcherQueue"/>.
/// </summary>
public sealed partial class ActionExecutionViewModel : ObservableObject
{
    // ── Process elevation ─────────────────────────────────────────────────────
    // Evaluated once per process lifetime; elevation cannot change mid-session.

    private static readonly bool s_isElevated =
        new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);

    // ── Static brush palette ── one allocation per outcome tier, ever ─────────

    private static readonly SolidColorBrush s_successBrush  = Mk(255,  87, 214, 141); // Green  #57D68D
    private static readonly SolidColorBrush s_warningBrush  = Mk(255, 255, 179,  71); // Orange #FFB347
    private static readonly SolidColorBrush s_failureBrush  = Mk(255, 255, 107, 107); // Red    #FF6B6B
    private static readonly SolidColorBrush s_infoBrush     = Mk(255,  78, 161, 255); // Blue   #4EA1FF
    private static readonly SolidColorBrush s_subtleBrush   = Mk(128, 180, 180, 180); // Gray (muted)

    private static SolidColorBrush Mk(byte a, byte r, byte g, byte b) =>
        new(Color.FromArgb(a, r, g, b));

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly string                 _actionId;
    private readonly string                 _actionTitle;
    private readonly bool                   _requiresConfirmation;
    private readonly string?                _confirmationMessage;
    private readonly IActionExecutionEngine _engine;
    private readonly DispatcherQueue?       _dispatcherQueue;

    // ── Mutable runtime state ─────────────────────────────────────────────────

    private CancellationTokenSource? _cts;
    private string?                  _rollbackToken;

    /// <summary>
    /// History record ID of the most recent successful execution that has a
    /// rollback snapshot. Set when a successful result with
    /// <see cref="ActionExecutionResult.CanRollback"/> is recorded; cleared
    /// when the rollback is performed or the card is reset.
    ///
    /// Used by <see cref="RecordToHistory"/> to mark the original execution
    /// record as rolled-back after a successful rollback completes.
    /// </summary>
    private string? _lastHistoryRecordId;

    // ── Observable properties ─────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IdleControlsVisibility))]
    [NotifyPropertyChangedFor(nameof(ConfirmationVisibility))]
    [NotifyPropertyChangedFor(nameof(ProgressVisibility))]
    [NotifyPropertyChangedFor(nameof(ResultVisibility))]
    [NotifyPropertyChangedFor(nameof(RollbackVisibility))]   // RollbackVisibility = CanRollback && State==Succeeded — CanRollback is set before State, so State must also notify
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(StateGlyph))]
    [NotifyPropertyChangedFor(nameof(StateBrush))]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    private ActionExecutionState _state = ActionExecutionState.Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RollbackVisibility))]
    private bool _canRollback;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // ── Log entries (ObservableCollection for live-streaming) ─────────────────

    /// <summary>
    /// Live-updating log of execution steps.
    /// Entries arrive in real time as the executor writes them, not just at the
    /// end of execution. Empty between runs.
    /// </summary>
    public ObservableCollection<ActionLogEntryViewModel> LogEntries { get; } = new();

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new execution ViewModel for the action identified by
    /// <paramref name="actionId"/>.
    ///
    /// Must be called on the UI thread so <see cref="DispatcherQueue"/> can be
    /// captured for live log marshalling.
    /// </summary>
    public ActionExecutionViewModel(
        string                 actionId,
        string                 actionTitle,
        bool                   requiresConfirmation,
        string?                confirmationMessage)
    {
        _actionId             = actionId;
        _actionTitle          = actionTitle;
        _requiresConfirmation = requiresConfirmation;
        _confirmationMessage  = confirmationMessage;
        _engine               = AppServices.ExecutionEngine;
        _dispatcherQueue      = DispatcherQueue.GetForCurrentThread();

        // Keep LogVisibility in sync whenever items are added or cleared.
        LogEntries.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(LogVisibility));
    }

    // ── Identity / capability ─────────────────────────────────────────────────

    /// <summary>
    /// True when a registered executor exists for this action.
    /// Drives whether Run/Preview controls are shown or a "Coming soon" chip
    /// is shown instead.
    /// </summary>
    public bool IsExecutorAvailable => _engine.IsSupported(_actionId);

    /// <summary>
    /// The static confirmation warning text from the <see cref="SystemAction"/>,
    /// shown in the confirmation panel before the user commits to a risky action.
    /// </summary>
    public string? ConfirmationMessage => _confirmationMessage;

    // ── Visibility tokens ─────────────────────────────────────────────────────

    /// <summary>Run + Preview buttons. Shown only when Idle and an executor exists.</summary>
    public Visibility IdleControlsVisibility =>
        State == ActionExecutionState.Idle && IsExecutorAvailable
            ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>"Coming soon" chip. Shown when no executor is registered.</summary>
    public Visibility NotImplementedVisibility =>
        !IsExecutorAvailable ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Confirmation panel. Shown when awaiting user acknowledgement.</summary>
    public Visibility ConfirmationVisibility =>
        State == ActionExecutionState.ConfirmationPending
            ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Progress ring + status label. Shown while Running or DryRunning.</summary>
    public Visibility ProgressVisibility =>
        State is ActionExecutionState.Running or ActionExecutionState.DryRunning
            ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Result panel (icon + message). Shown after any terminal state.</summary>
    public Visibility ResultVisibility =>
        State is ActionExecutionState.Succeeded
                 or ActionExecutionState.PartiallySucceeded
                 or ActionExecutionState.DryRunComplete
                 or ActionExecutionState.Failed
                 or ActionExecutionState.RolledBack
            ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Rollback button. Shown only when the last execution succeeded and the
    /// executor captured a rollback snapshot.
    /// </summary>
    public Visibility RollbackVisibility =>
        CanRollback && State == ActionExecutionState.Succeeded
            ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Execution log panel. Shown whenever at least one log entry exists.
    /// Persists after execution so the user can review what happened.
    /// </summary>
    public Visibility LogVisibility =>
        LogEntries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    // ── State → visuals ───────────────────────────────────────────────────────

    /// <summary>True while an execution or dry-run is in progress.</summary>
    public bool IsRunning =>
        State is ActionExecutionState.Running or ActionExecutionState.DryRunning
                 or ActionExecutionState.RollingBack;

    /// <summary>Segoe MDL2 glyph matching the current terminal state.</summary>
    public string StateGlyph => State switch
    {
        ActionExecutionState.Succeeded         => "",  // Checkmark
        ActionExecutionState.RolledBack        => "",  // Sync / undo
        ActionExecutionState.DryRunComplete    => "",  // Info
        ActionExecutionState.PartiallySucceeded => "", // Alert
        ActionExecutionState.Failed            => "",  // Error badge
        _                                      => "",  // Info fallback
    };

    /// <summary>Foreground brush matched to the current terminal state.</summary>
    public SolidColorBrush StateBrush => State switch
    {
        ActionExecutionState.Succeeded          => s_successBrush,
        ActionExecutionState.RolledBack         => s_successBrush,
        ActionExecutionState.DryRunComplete     => s_infoBrush,
        ActionExecutionState.PartiallySucceeded => s_warningBrush,
        ActionExecutionState.Failed             => s_failureBrush,
        _                                       => s_subtleBrush,
    };

    /// <summary>Short state label shown next to the result glyph.</summary>
    public string StateLabel => State switch
    {
        ActionExecutionState.Succeeded          => "Done",
        ActionExecutionState.RolledBack         => "Undone",
        ActionExecutionState.DryRunComplete     => "Preview",
        ActionExecutionState.PartiallySucceeded => "Partial",
        ActionExecutionState.Failed             => "Failed",
        _                                       => string.Empty,
    };

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Requests live execution. If the action requires confirmation, transitions
    /// to <see cref="ActionExecutionState.ConfirmationPending"/> and waits;
    /// otherwise starts execution immediately.
    /// </summary>
    [RelayCommand]
    private async Task RunAsync()
    {
        if (_requiresConfirmation)
        {
            State = ActionExecutionState.ConfirmationPending;
            return;
        }
        await ExecuteCoreAsync(isDryRun: false, confirmationGranted: false);
    }

    /// <summary>
    /// Runs a dry-run simulation. Never modifies the system; shows a preview
    /// of what would happen. No confirmation needed since nothing changes.
    /// </summary>
    [RelayCommand]
    private Task DryRunAsync() =>
        ExecuteCoreAsync(isDryRun: true, confirmationGranted: false);

    /// <summary>
    /// Confirms the pending risky action and starts live execution.
    /// Only valid in <see cref="ActionExecutionState.ConfirmationPending"/>.
    /// </summary>
    [RelayCommand]
    private Task ConfirmAsync() =>
        ExecuteCoreAsync(isDryRun: false, confirmationGranted: true);

    /// <summary>
    /// Dismisses the confirmation panel and returns to Idle without running.
    /// </summary>
    [RelayCommand]
    private void CancelConfirmation() => State = ActionExecutionState.Idle;

    /// <summary>
    /// Cancels a running execution by signalling its <see cref="CancellationToken"/>.
    /// Has no effect if no execution is in progress.
    /// </summary>
    [RelayCommand]
    private void CancelExecution() => _cts?.Cancel();

    /// <summary>
    /// Rolls back the last successful execution using its captured snapshot token.
    /// Only available when <see cref="CanRollback"/> is <see langword="true"/>.
    /// </summary>
    [RelayCommand]
    private async Task RollbackAsync()
    {
        if (_rollbackToken is null || !CanRollback) return;

        State         = ActionExecutionState.RollingBack;
        StatusMessage = "Rolling back…";
        LogEntries.Clear();
        CanRollback   = false;

        var log     = BuildLiveLog();
        var context = new ActionExecutionContext
        {
            IsDryRun            = false,
            IsElevated          = false,
            ConfirmationGranted = true,
            RequestedBy         = "user",
            Log                 = log,
        };

        ActionExecutionResult result;
        try
        {
            result = await _engine
                .RollbackAsync(_actionId, _rollbackToken, context)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Unexpected engine throw during rollback — surface a Failed state
            // so the UI doesn't stay stuck in the RollingBack spinner.
            StatusMessage = $"Rollback failed: {ex.Message}";
            State         = ActionExecutionState.Failed;
            System.Diagnostics.Debug.WriteLine($"[ActionExecution] RollbackAsync threw: {ex}");
            return;
        }

        ApplyResult(result, isRollback: true);
    }

    /// <summary>
    /// Resets to <see cref="ActionExecutionState.Idle"/> and clears all
    /// execution state so the action can be run again.
    /// </summary>
    [RelayCommand]
    private void ResetToIdle()
    {
        _cts?.Cancel();
        _cts                 = null;
        _rollbackToken       = null;
        _lastHistoryRecordId = null;
        CanRollback          = false;
        StatusMessage        = string.Empty;
        LogEntries.Clear();
        State = ActionExecutionState.Idle;
    }

    // ── Core execution ────────────────────────────────────────────────────────

    private async Task ExecuteCoreAsync(bool isDryRun, bool confirmationGranted)
    {
        // Cancel any previous in-flight call (defensive; shouldn't happen since
        // the Run button is hidden while running).
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        State         = isDryRun ? ActionExecutionState.DryRunning : ActionExecutionState.Running;
        StatusMessage = isDryRun ? "Simulating…" : "Running…";
        CanRollback   = false;
        _rollbackToken = null;
        LogEntries.Clear();

        var log     = BuildLiveLog();
        var context = new ActionExecutionContext
        {
            IsDryRun            = isDryRun,
            IsElevated          = s_isElevated,
            ConfirmationGranted = confirmationGranted || !_requiresConfirmation,
            RequestedBy         = "user",
            Log                 = log,
        };

        ActionExecutionResult result;
        try
        {
            result = await _engine
                .ExecuteAsync(_actionId, context, _cts.Token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // User cancelled — return to idle with no error message.
            State         = ActionExecutionState.Idle;
            StatusMessage = string.Empty;
            return;
        }
        catch (Exception ex)
        {
            // Unexpected engine throw (e.g. executor constructor fault).
            // Surface a Failed state with a readable message so the spinner clears.
            StatusMessage = $"Unexpected error: {ex.Message}";
            State         = ActionExecutionState.Failed;
            System.Diagnostics.Debug.WriteLine($"[ActionExecution] ExecuteCoreAsync threw: {ex}");
            return;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }

        ApplyResult(result, isRollback: false);
    }

    private void ApplyResult(ActionExecutionResult result, bool isRollback)
    {
        StatusMessage  = result.Message;
        _rollbackToken = result.RollbackToken;
        CanRollback    = result.CanRollback && !isRollback;

        State = result.Status switch
        {
            ActionExecutionStatus.Success          =>
                isRollback ? ActionExecutionState.RolledBack : ActionExecutionState.Succeeded,
            ActionExecutionStatus.PartialSuccess   => ActionExecutionState.PartiallySucceeded,
            ActionExecutionStatus.DryRunSuccess    => ActionExecutionState.DryRunComplete,
            ActionExecutionStatus.Failed           => ActionExecutionState.Failed,
            ActionExecutionStatus.Cancelled        => ActionExecutionState.Idle,
            ActionExecutionStatus.RequiresElevation    => ActionExecutionState.Failed,
            ActionExecutionStatus.RequiresConfirmation => ActionExecutionState.ConfirmationPending,
            ActionExecutionStatus.ExecutorNotFound     => ActionExecutionState.Failed,
            _                                          => ActionExecutionState.Failed,
        };

        // For pre-flight failures the live callback never fired (executor wasn't
        // called), so result.Log contains entries that LogEntries doesn't yet have.
        if (LogEntries.Count == 0)
        {
            foreach (var entry in result.Log)
                LogEntries.Add(new ActionLogEntryViewModel(entry));
        }

        // Persist the result to operational history (fire-and-forget).
        RecordToHistory(result, isRollback);
    }

    // ── Live log helper ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds an <see cref="ActionExecutionLog"/> whose callback marshals each
    /// entry to the UI thread and appends it to <see cref="LogEntries"/>.
    /// </summary>
    private ActionExecutionLog BuildLiveLog() =>
        new(entry =>
        {
            if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
            {
                // Already on UI thread (common for Phase-1 synchronous executors).
                LogEntries.Add(new ActionLogEntryViewModel(entry));
            }
            else
            {
                _dispatcherQueue.TryEnqueue(() =>
                    LogEntries.Add(new ActionLogEntryViewModel(entry)));
            }
        });

    // ── History recording ─────────────────────────────────────────────────────

    /// <summary>
    /// Persists the execution result to <see cref="IOperationHistoryService"/>
    /// as a fire-and-forget background task.
    ///
    /// Pre-flight failures (executor not found, requires elevation, requires
    /// confirmation) are not recorded — the executor was never reached, so
    /// there is no meaningful system-level event to audit.
    ///
    /// For successful rollbacks, the original execution record is also updated
    /// via <see cref="IOperationHistoryService.MarkRolledBackAsync"/> so the
    /// history reflects the full forward-and-back lifecycle.
    ///
    /// Declared <c>async void</c> (intentionally fire-and-forget from the call
    /// site) with an explicit try/catch so I/O failures in the history store are
    /// logged rather than becoming unobserved task exceptions.
    /// </summary>
    private async void RecordToHistory(ActionExecutionResult result, bool isRollback)
    {
        // Skip pre-flight failures — no executor was invoked, nothing happened.
        if (result.Status is ActionExecutionStatus.ExecutorNotFound
                          or ActionExecutionStatus.RequiresElevation
                          or ActionExecutionStatus.RequiresConfirmation)
            return;

        // Extract Warning and Error log lines for the persistent record.
        // The full per-file log is transient and not stored.
        var warnings = result.Log
            .Where(e => e.Level == ActionLogLevel.Warning)
            .Select(e => e.Message)
            .ToList();
        var errors = result.Log
            .Where(e => e.Level == ActionLogLevel.Error)
            .Select(e => e.Message)
            .ToList();

        var record = new OperationRecord
        {
            ActionId        = _actionId,
            ActionTitle     = _actionTitle,
            ExecutedAt      = result.ExecutedAt,
            DurationMs      = (long)result.Duration.TotalMilliseconds,
            Status          = result.Status.ToString(),
            IsSuccess       = result.IsSuccess,
            IsDryRun        = result.IsDryRun,
            IsRollback      = isRollback,
            Message         = result.Message,
            CanRollback     = result.CanRollback,
            RollbackToken   = result.RollbackToken,
            TotalLogEntries = result.Log.Count,
            Warnings        = warnings,
            Errors          = errors,
        };

        // Capture the record ID before the async write so the rollback path
        // can reference it synchronously without waiting for the I/O to finish.
        if (!isRollback && result.CanRollback)
            _lastHistoryRecordId = record.Id;

        var svc = AppServices.HistoryService;

        try
        {
            await svc.RecordAsync(record);

            // When a rollback succeeds, mark the original forward-execution record.
            if (isRollback && result.IsSuccess && _lastHistoryRecordId is not null)
            {
                var originalId       = _lastHistoryRecordId;
                _lastHistoryRecordId = null;
                await svc.MarkRolledBackAsync(originalId, DateTimeOffset.Now);
            }
        }
        catch (Exception ex)
        {
            // History write failed (e.g. SQLite locked, disk full).
            // The operation itself completed successfully — this only affects
            // the audit trail and replay view, not the user-visible outcome.
            System.Diagnostics.Debug.WriteLine($"[ActionHistory] Persist failed: {ex}");
        }
    }
}
