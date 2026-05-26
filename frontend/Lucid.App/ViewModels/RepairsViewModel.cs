using System.Collections.ObjectModel;
using System.Security.Principal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Execution;
using Lucid.Services.History;
using Lucid.Services.Intelligence;   // ActionRisk
using Lucid.Services.Learning;
using Lucid.Services.Repair;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Lucid.ViewModels;

// ═══════════════════════════════════════════════════════════════════════════
//  RepairsViewModel
//  Page-level ViewModel.  Owns the list of repair card ViewModels and
//  exposes the current elevation posture so the View can show a warning
//  banner when the app is not running as administrator.
// ═══════════════════════════════════════════════════════════════════════════

public sealed partial class RepairsViewModel : ObservableObject
{
    // ── Elevation state ───────────────────────────────────────────────────────

    /// <summary>True when the process was launched with administrator privileges.</summary>
    public bool IsElevated { get; }

    public Visibility ElevationWarningVisibility =>
        IsElevated ? Visibility.Collapsed : Visibility.Visible;

    // ── Repair cards ──────────────────────────────────────────────────────────

    /// <summary>
    /// One card per repair action in the catalog, in logical execution order:
    /// quick/safe network tools first, heavier OS repair tools last.
    /// </summary>
    public IReadOnlyList<RepairCardViewModel> RepairCards { get; }

    // ── Construction ──────────────────────────────────────────────────────────

    public RepairsViewModel()
    {
        IsElevated = CheckElevation();

        // Build cards in logical display order — safe/quick tools first.
        var orderedIds = new[]
        {
            "action.network.flush-dns",
            "action.apps.reset-windows-store",
            "action.repair.sfc-scannow",
            "action.repair.dism-restore-health",
            "action.network.winsock-reset",
            "action.network.reset-adapter",
        };

        var cards = new List<RepairCardViewModel>(orderedIds.Length);
        foreach (var id in orderedIds)
        {
            var meta = RepairOperationCatalog.TryGet(id);
            if (meta is not null)
                cards.Add(new RepairCardViewModel(meta, IsElevated));
        }
        RepairCards = cards;
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    /// <summary>Cancel any in-progress repairs when the page is unloaded.</summary>
    public void Cleanup()
    {
        foreach (var card in RepairCards)
            card.Cancel();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool CheckElevation()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  RepairCardViewModel
//  Drives a single repair action card: metadata display, live log streaming,
//  execution state, and result presentation.
// ═══════════════════════════════════════════════════════════════════════════

public sealed partial class RepairCardViewModel : ObservableObject
{
    // ── Metadata (static, from catalog) ───────────────────────────────────────

    public RepairOperationMetadata Metadata         { get; }
    public string ActionId                          => Metadata.ActionId;
    public string DisplayName                       => Metadata.DisplayName;
    public string Purpose                           => Metadata.Purpose;
    public string HowItWorks                        => Metadata.HowItWorks;
    public string ExpectedDuration                  => Metadata.ExpectedDuration;
    public bool   MetadataRequiresRestart           => Metadata.RequiresRestart;
    public string RestartReason                     => Metadata.RestartReason ?? string.Empty;
    public string ReversibilityNote                 => Metadata.ReversibilityNote ?? string.Empty;
    public bool   IsElevationRequired               => _executorNeedsAdmin;
    public bool   IsAppsElevated                    { get; }

    // Whether the registered executor requires administrator privileges.
    // Resolved once at construction time so there are no per-property-access lookups.
    private readonly bool _executorNeedsAdmin;

    // Whether the registered executor requires explicit user confirmation.
    private readonly bool _executorNeedsConfirmation;

    // ── Risk display helpers ──────────────────────────────────────────────────

    public string RiskLabel => Metadata.Risk switch
    {
        ActionRisk.Safe     => "Safe",
        ActionRisk.Low      => "Low Risk",
        ActionRisk.Moderate => "Moderate",
        ActionRisk.High     => "High Risk",
        _                   => "Unknown",
    };

    public string RiskBrushKey => Metadata.Risk switch
    {
        ActionRisk.Safe     => "StatusGoodBrush",
        ActionRisk.Low      => "StatusInfoBrush",
        ActionRisk.Moderate => "StatusModerateBrush",
        ActionRisk.High     => "StatusCriticalBrush",
        _                   => "TextSecondaryBrush",
    };

    public string ElevationNote => IsAppsElevated
        ? "Running as administrator"
        : "Requires administrator — re-launch Lucid as admin to run this.";

    // ── Effectiveness badge (from learning service) ───────────────────────────

    /// <summary>
    /// Short label for the effectiveness badge chip, e.g. "Historically effective on this machine".
    /// Empty string when no warm data is available (badge is hidden).
    /// </summary>
    public string EffectivenessLabel { get; }

    /// <summary>
    /// Longer descriptive sentence for the badge tooltip, e.g. "Helped reduce CPU pressure ~18%".
    /// </summary>
    public string EffectivenessText { get; }

    /// <summary>Visibility of the effectiveness badge — visible only when IsWarmEnough.</summary>
    public Visibility EffectivenessVisible { get; }

    /// <summary>StaticResource brush key for the badge background.</summary>
    public string EffectivenessBrushKey { get; }

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunButtonVisibility))]
    [NotifyPropertyChangedFor(nameof(PreviewButtonVisibility))]
    [NotifyPropertyChangedFor(nameof(CancelButtonVisibility))]
    [NotifyPropertyChangedFor(nameof(LogVisibility))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HowItWorksVisibility))]
    private bool _isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultBannerVisibility))]
    [NotifyPropertyChangedFor(nameof(LogVisibility))]                // LogVisibility = IsRunning || HasResult — notify on both fields
    [NotifyPropertyChangedFor(nameof(ResultRestartWarningVisibility))] // ResultRestartWarningVisibility = HasResult && ResultRequiresRestart — notify on both fields
    private bool _hasResult;

    [ObservableProperty] private string _resultMessage     = string.Empty;
    [ObservableProperty] private bool   _resultIsSuccess;
    [ObservableProperty] private bool   _resultIsPartial;
    [ObservableProperty] private bool   _resultIsFailure;
    [ObservableProperty] private bool   _resultIsCancelled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultRestartWarningVisibility))]
    private bool _resultRequiresRestart;

    // ── Visibility computed properties ────────────────────────────────────────

    public Visibility RunButtonVisibility     => Vis(!IsRunning);
    public Visibility PreviewButtonVisibility => Vis(!IsRunning);
    public Visibility CancelButtonVisibility  => Vis(IsRunning);
    public Visibility HowItWorksVisibility    => Vis(IsExpanded);
    public Visibility LogVisibility           => Vis(IsRunning || HasResult);
    public Visibility ResultBannerVisibility  => Vis(HasResult);
    public Visibility MetadataRestartWarningVisibility =>
        Vis(Metadata.RequiresRestart);
    public Visibility ResultRestartWarningVisibility =>
        Vis(HasResult && ResultRequiresRestart);
    public Visibility ElevationBlockedVisibility =>
        Vis(!IsAppsElevated && _executorNeedsAdmin);

    private static Visibility Vis(bool show) =>
        show ? Visibility.Visible : Visibility.Collapsed;

    // ── Live log ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Live-streaming log entries produced during execution.
    /// Updated from the executor's streaming callback, marshalled to the UI thread.
    /// </summary>
    public ObservableCollection<ActionLogEntryViewModel> LogEntries { get; } = new();

    // ── Callbacks (set by View) ───────────────────────────────────────────────

    /// <summary>
    /// Called when the executor's <see cref="IActionExecutor.RequiresConfirmation"/>
    /// is true. The view shows a ContentDialog and returns true if the user confirmed.
    /// </summary>
    public Func<string, Task<bool>>? RequestConfirmationAsync { get; set; }

    /// <summary>
    /// Called after a non-dry-run execution completes so the narrative engine
    /// can incorporate recent repair activity into its prose.
    /// Signature: (actionId, displayName, succeeded, requiresRestart)
    /// </summary>
    public Action<string, string, bool, bool>? NotifyRepairCompleted { get; set; }

    // ── Private state ─────────────────────────────────────────────────────────

    private CancellationTokenSource? _cts;
    private readonly DispatcherQueue _dispatcher;

    // ── Construction ──────────────────────────────────────────────────────────

    public RepairCardViewModel(RepairOperationMetadata metadata, bool isElevated)
    {
        Metadata       = metadata;
        IsAppsElevated = isElevated;

        // Resolve admin requirement from the registered executor, falling back to
        // Risk-based inference (Moderate/High always need admin).
        if (AppServices.ExecutorRegistry.TryGetExecutor(metadata.ActionId, out var exec)
            && exec is not null)
        {
            _executorNeedsAdmin        = exec.RequiredPrivilege == ActionPrivilegeLevel.Administrator;
            _executorNeedsConfirmation = exec.RequiresConfirmation;
        }
        else
        {
            _executorNeedsAdmin        = metadata.Risk >= ActionRisk.Moderate;
            _executorNeedsConfirmation = metadata.Risk >= ActionRisk.Moderate;
        }

        _dispatcher = DispatcherQueue.GetForCurrentThread()
                   ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        // Populate effectiveness badge from the learning service (cold-start safe).
        var profile = AppServices.LearningService.GetProfile(metadata.ActionId);
        if (profile is { IsWarmEnough: true })
        {
            EffectivenessLabel   = profile.EffectivenessLabel;
            EffectivenessText    = profile.SummaryText;
            EffectivenessVisible = Visibility.Visible;
            EffectivenessBrushKey = profile.EffectivenessRate >= 0.70
                ? "StatusGoodBrush"
                : profile.EffectivenessRate >= 0.30
                    ? "StatusModerateBrush"
                    : "StatusCriticalBrush";
        }
        else
        {
            EffectivenessLabel    = string.Empty;
            EffectivenessText     = string.Empty;
            EffectivenessVisible  = Visibility.Collapsed;
            EffectivenessBrushKey = "TextSecondaryBrush";
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private Task RunAsync() => ExecuteInternalAsync(dryRun: false);

    [RelayCommand]
    private Task PreviewAsync() => ExecuteInternalAsync(dryRun: true);

    [RelayCommand]
    public void Cancel()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void ToggleHowItWorks() => IsExpanded = !IsExpanded;

    // ── Execution ─────────────────────────────────────────────────────────────

    private async Task ExecuteInternalAsync(bool dryRun)
    {
        if (IsRunning) return;

        // ── Confirmation gate (for Winsock / Network reset) ───────────────────
        if (!dryRun && _executorNeedsConfirmation)
        {
            if (RequestConfirmationAsync is not null)
            {
                bool confirmed = await RequestConfirmationAsync(
                    $"Run {Metadata.DisplayName}?\n\n{Metadata.ReversibilityNote}");
                if (!confirmed) return;
            }
        }

        // ── Reset UI state ────────────────────────────────────────────────────
        LogEntries.Clear();
        HasResult            = false;
        ResultIsSuccess      = false;
        ResultIsPartial      = false;
        ResultIsFailure      = false;
        ResultIsCancelled    = false;
        ResultRequiresRestart = false;
        ResultMessage        = string.Empty;
        IsRunning            = true;

        // ── Build execution context with live-streaming log ───────────────────
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var log = new ActionExecutionLog(entry =>
        {
            // The executor runs on a background thread; marshal back to UI.
            _dispatcher.TryEnqueue(() =>
                LogEntries.Add(new ActionLogEntryViewModel(entry)));
        });

        var context = new ActionExecutionContext
        {
            IsDryRun            = dryRun,
            IsElevated          = IsAppsElevated,
            ConfirmationGranted = !_executorNeedsConfirmation || dryRun,
            RequestedBy         = "user",
            Log                 = log,
        };

        // ── Execute ───────────────────────────────────────────────────────────
        ActionExecutionResult result;
        try
        {
            result = await AppServices.ExecutionEngine
                .ExecuteAsync(ActionId, context, ct)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            result = ActionExecutionResult.Failed(
                ActionId,
                $"Unexpected error: {ex.Message}",
                TimeSpan.Zero,
                log.Build(),
                ex.ToString());
        }
        finally
        {
            _cts.Dispose();
            _cts    = null;
            IsRunning = false;
        }

        // ── Surface result ────────────────────────────────────────────────────
        ResultMessage     = result.Message;
        ResultIsSuccess   = result.Status == ActionExecutionStatus.Success;
        ResultIsPartial   = result.Status == ActionExecutionStatus.PartialSuccess;
        ResultIsFailure   = result.Status == ActionExecutionStatus.Failed;
        ResultIsCancelled = result.Status == ActionExecutionStatus.Cancelled;

        // Flag restart required if the catalog metadata says so AND execution succeeded.
        ResultRequiresRestart =
            Metadata.RequiresRestart &&
            (ResultIsSuccess || ResultIsPartial);

        HasResult = true;

        // ── Persist to operation history (best-effort) ────────────────────────
        if (!dryRun)
        {
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
                ActionId        = ActionId,
                ActionTitle     = DisplayName,
                ExecutedAt      = result.ExecutedAt,
                DurationMs      = (long)result.Duration.TotalMilliseconds,
                Status          = result.Status.ToString(),
                IsSuccess       = result.IsSuccess,
                IsDryRun        = false,
                IsRollback      = false,
                Message         = result.Message,
                CanRollback     = result.CanRollback,
                RollbackToken   = result.RollbackToken,
                TotalLogEntries = result.Log.Count,
                Warnings        = warnings,
                Errors          = errors,
            };

            // History persistence is best-effort: a write failure (disk full,
            // SQLite locked) must not surface as an unhandled exception or block
            // the UI feedback path.  The operation outcome is already visible to
            // the user; only the audit trail is affected.
            try
            {
                await AppServices.HistoryService.RecordAsync(record);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RepairHistory] RecordAsync failed: {ex}");
            }

            // ── Notify narrative engine ───────────────────────────────────────
            if (result.Status is ActionExecutionStatus.Success
                              or ActionExecutionStatus.PartialSuccess
                              or ActionExecutionStatus.Failed)
            {
                NotifyRepairCompleted?.Invoke(
                    ActionId,
                    DisplayName,
                    result.IsSuccess,
                    ResultRequiresRestart);
            }
        }
    }
}
