using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Governance;
using Microsoft.UI.Xaml;

namespace Lucid.ViewModels;

// ── Sub-ViewModels ─────────────────────────────────────────────────────────────

/// <summary>Presentation model for a single active workload row.</summary>
public sealed class ActiveWorkloadViewModel
{
    public string Name          { get; init; } = "";
    public string CategoryLabel { get; init; } = "";
    public string PriorityLabel { get; init; } = "";
    public string StartedText   { get; init; } = "";
    public string ElapsedText   { get; init; } = "";

    public Visibility ForegroundBadgeVisibility { get; init; }
    public Visibility BackgroundBadgeVisibility { get; init; }
    public Visibility IdleOnlyBadgeVisibility   { get; init; }

    internal ActiveWorkloadViewModel(ActiveWorkloadEntry e)
    {
        Name          = e.Name;
        CategoryLabel = WorkloadClassifier.GetDisplayName(e.Category);
        PriorityLabel = e.Priority switch
        {
            WorkloadPriority.Foreground => "Foreground",
            WorkloadPriority.Background => "Background",
            WorkloadPriority.IdleOnly   => "Idle Only",
            _                           => "Unknown",
        };
        var elapsed   = DateTimeOffset.Now - e.StartedAt;
        StartedText   = $"Started {e.StartedAt.LocalDateTime:h:mm:ss tt}";
        ElapsedText   = elapsed.TotalSeconds < 60
            ? $"{(int)elapsed.TotalSeconds}s"
            : $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";

        ForegroundBadgeVisibility = e.Priority == WorkloadPriority.Foreground ? Visibility.Visible : Visibility.Collapsed;
        BackgroundBadgeVisibility = e.Priority == WorkloadPriority.Background  ? Visibility.Visible : Visibility.Collapsed;
        IdleOnlyBadgeVisibility   = e.Priority == WorkloadPriority.IdleOnly    ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>Presentation model for a single deferred workload row.</summary>
public sealed class DeferredWorkloadViewModel
{
    public string Name          { get; init; } = "";
    public string CategoryLabel { get; init; } = "";
    public string DeferredText  { get; init; } = "";
    public string Reason        { get; init; } = "";

    internal DeferredWorkloadViewModel(DeferredWorkloadEntry e)
    {
        Name          = e.Name;
        CategoryLabel = WorkloadClassifier.GetDisplayName(e.Category);
        var waited    = DateTimeOffset.Now - e.DeferredAt;
        DeferredText  = waited.TotalSeconds < 60
            ? $"Waiting {(int)waited.TotalSeconds}s"
            : $"Waiting {(int)waited.TotalMinutes}m";
        Reason        = e.DeferralReason;
    }
}

// ── Snapshot record for background reads ──────────────────────────────────────

/// <summary>Plain data holder for a governance read — no UI thread required.</summary>
internal sealed record GovernanceReadResult(
    ResourceBudgetSnapshot              Snapshot,
    IReadOnlyList<ActiveWorkloadEntry>  Active,
    IReadOnlyList<DeferredWorkloadEntry> Deferred);

// ── Main ViewModel ─────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for the Runtime Governance page.
///
/// Subscribes to <see cref="IRuntimeGovernanceService.ModeChanged"/> for
/// live mode-banner updates, and refreshes workload lists on user demand or
/// on navigation.
///
/// All observable property writes happen on the UI thread:
///   - RefreshAsync fetches data on a thread-pool thread, then applies on UI thread.
///   - ModeChanged fires on the UI thread (RuntimeGovernanceService posts via dispatcher).
/// </summary>
public sealed partial class RuntimeGovernanceViewModel : ObservableObject
{
    private readonly IRuntimeGovernanceService _governance;

    // ── Observable properties ──────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNormalVisibility))]
    [NotifyPropertyChangedFor(nameof(IsNotNormalVisibility))]
    [NotifyPropertyChangedFor(nameof(ModeBadgeColor))]
    private string _modeLabel = "Normal";

    [ObservableProperty]
    private string _modeDescription = "Background analysis running normally.";

    [ObservableProperty]
    private string _throttlingReasons = "No throttling conditions detected.";

    [ObservableProperty]
    private string _telemetryInterval = "1.5 s";

    [ObservableProperty]
    private string _processInterval = "4.5 s";

    [ObservableProperty]
    private string _backgroundSlots = "0 / 3";

    [ObservableProperty]
    private string _activeWorkloadCount = "0";

    [ObservableProperty]
    private string _deferredWorkloadCount = "0";

    [ObservableProperty]
    private string _snapshotTime = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveWorkloadsVisibility))]
    [NotifyPropertyChangedFor(nameof(NoActiveWorkloadsVisibility))]
    private bool _hasActiveWorkloads;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDeferredWorkloadsVisibility))]
    [NotifyPropertyChangedFor(nameof(NoDeferredWorkloadsVisibility))]
    private bool _hasDeferredWorkloads;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingVisibility))]
    [NotifyPropertyChangedFor(nameof(ContentVisibility))]
    private bool _isLoading = true;

    // ── Derived Visibility ─────────────────────────────────────────────────────

    public Visibility LoadingVisibility             => IsLoading               ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ContentVisibility             => !IsLoading              ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsNormalVisibility            => ModeLabel == "Normal"   ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsNotNormalVisibility         => ModeLabel != "Normal"   ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasActiveWorkloadsVisibility  => HasActiveWorkloads      ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoActiveWorkloadsVisibility   => !HasActiveWorkloads     ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasDeferredWorkloadsVisibility => HasDeferredWorkloads   ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoDeferredWorkloadsVisibility  => !HasDeferredWorkloads  ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Accent color for the mode badge — green for Normal, amber otherwise.</summary>
    public string ModeBadgeColor => ModeLabel == "Normal" ? "#22C55E" : "#F59E0B";

    // ── Collections ────────────────────────────────────────────────────────────

    public ObservableCollection<ActiveWorkloadViewModel>   ActiveWorkloads   { get; } = [];
    public ObservableCollection<DeferredWorkloadViewModel> DeferredWorkloads { get; } = [];

    // ── Commands ───────────────────────────────────────────────────────────────

    public IAsyncRelayCommand RefreshCommand { get; }

    // ── Construction ──────────────────────────────────────────────────────────

    public RuntimeGovernanceViewModel(IRuntimeGovernanceService governance)
    {
        ArgumentNullException.ThrowIfNull(governance);
        _governance = governance;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);

        // ModeChanged is already raised on the UI thread by RuntimeGovernanceService.
        _governance.ModeChanged += OnModeChanged;
    }

    // ── Initialization ─────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        IsLoading = true;
        await RefreshAsync().ConfigureAwait(true); // stay on UI thread after await
        IsLoading = false;
    }

    // ── Refresh ────────────────────────────────────────────────────────────────

    private async Task RefreshAsync()
    {
        // Read governance data on a thread-pool thread (lock acquisition inside
        // ConcurrencyBudget is cheap but caller may be on the UI thread).
        var result = await Task.Run(ReadGovernanceData).ConfigureAwait(true);

        // ConfigureAwait(true) ensures we're back on the UI thread here.
        ApplyResult(result);
    }

    /// <summary>
    /// Pure data read — no UI. Safe to call from any thread.
    /// </summary>
    private GovernanceReadResult ReadGovernanceData() => new(
        Snapshot: _governance.GetSnapshot(),
        Active:   _governance.GetActiveWorkloads(),
        Deferred: _governance.GetDeferredWorkloads());

    /// <summary>
    /// Applies a snapshot to all observable properties.
    /// MUST be called from the UI thread.
    /// </summary>
    private void ApplyResult(GovernanceReadResult r)
    {
        var s = r.Snapshot;

        ModeLabel             = RuntimePressureAnalyzer.DescribeMode(s.Mode);
        ModeDescription       = AdaptiveSchedulingPolicy.GetDeferralDescription(s.Mode);
        ThrottlingReasons     = RuntimePressureAnalyzer.DescribeReasons(s.Reasons);
        TelemetryInterval     = $"{s.TelemetryInterval.TotalSeconds:F1} s";
        ProcessInterval       = $"{s.ProcessSampleInterval.TotalSeconds:F1} s";
        BackgroundSlots       = $"{s.UsedBackgroundSlots} / {s.MaxConcurrentBackground}";
        ActiveWorkloadCount   = s.ActiveWorkloadCount.ToString();
        DeferredWorkloadCount = r.Deferred.Count.ToString();
        SnapshotTime          = $"Updated {s.SnapshotAt.LocalDateTime:h:mm:ss tt}";

        ActiveWorkloads.Clear();
        foreach (var w in r.Active)
            ActiveWorkloads.Add(new ActiveWorkloadViewModel(w));

        DeferredWorkloads.Clear();
        foreach (var d in r.Deferred)
            DeferredWorkloads.Add(new DeferredWorkloadViewModel(d));

        HasActiveWorkloads   = ActiveWorkloads.Count  > 0;
        HasDeferredWorkloads = DeferredWorkloads.Count > 0;
    }

    // ── Event handler ──────────────────────────────────────────────────────────

    /// <summary>
    /// Called on the UI thread whenever the runtime mode changes.
    /// Updates the mode banner immediately, then schedules a full data refresh.
    /// </summary>
    private void OnModeChanged(object? sender, RuntimeModeChangedEventArgs e)
    {
        ModeLabel         = RuntimePressureAnalyzer.DescribeMode(e.NewMode);
        ModeDescription   = AdaptiveSchedulingPolicy.GetDeferralDescription(e.NewMode);
        ThrottlingReasons = e.Description;

        // Fire-and-forget refresh to update slot counts and polling intervals.
        _ = RefreshAsync();
    }

    // ── Cleanup ────────────────────────────────────────────────────────────────

    public void Cleanup()
    {
        _governance.ModeChanged -= OnModeChanged;
    }
}
