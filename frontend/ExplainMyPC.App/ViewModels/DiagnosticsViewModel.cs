using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplainMyPC.Services.Diagnostics;
using Microsoft.UI.Xaml;

namespace ExplainMyPC.ViewModels;

// ── Sub-ViewModels ─────────────────────────────────────────────────────────────

/// <summary>Presentation model for a single service health card.</summary>
public sealed class ServiceHealthCardViewModel
{
    public string Name              { get; init; } = "";
    public string StatusLabel       { get; init; } = "";
    public string UptimeText        { get; init; } = "";
    public string FailureText       { get; init; } = "";
    public string LastEventText     { get; init; } = "";
    public string StatusColor       { get; init; } = "#6B7280";

    public Visibility HealthyBadge  { get; init; }
    public Visibility DegradedBadge { get; init; }
    public Visibility FailedBadge   { get; init; }
    public Visibility UnknownBadge  { get; init; }

    internal ServiceHealthCardViewModel(ServiceHealthRecord r)
    {
        Name          = r.ServiceName;
        StatusLabel   = r.StatusLabel;
        var uptime    = r.Uptime;
        UptimeText    = uptime.TotalHours >= 1
            ? $"{(int)uptime.TotalHours}h {uptime.Minutes}m"
            : $"{uptime.Minutes}m {uptime.Seconds}s";
        FailureText   = r.FailureCount == 0
            ? "No failures"
            : $"{r.FailureCount} failure{(r.FailureCount == 1 ? "" : "s")}";
        LastEventText = r.LastFailureAt.HasValue
            ? $"Last issue {r.LastFailureAt.Value.LocalDateTime:h:mm tt}"
            : r.LastSuccessAt == DateTimeOffset.MinValue
                ? "Not yet observed"
                : $"Last OK {r.LastSuccessAt.LocalDateTime:h:mm tt}";

        StatusColor   = r.Status switch
        {
            ServiceHealthStatus.Healthy    => "#22C55E",
            ServiceHealthStatus.Degraded   => "#F59E0B",
            ServiceHealthStatus.Failed     => "#EF4444",
            ServiceHealthStatus.Recovering => "#3B82F6",
            _                              => "#6B7280",
        };

        HealthyBadge  = r.Status == ServiceHealthStatus.Healthy    ? Visibility.Visible : Visibility.Collapsed;
        DegradedBadge = r.Status == ServiceHealthStatus.Degraded   ? Visibility.Visible : Visibility.Collapsed;
        FailedBadge   = r.Status is ServiceHealthStatus.Failed
                                  or ServiceHealthStatus.Recovering ? Visibility.Visible : Visibility.Collapsed;
        UnknownBadge  = r.Status == ServiceHealthStatus.Unknown    ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>Presentation model for a single diagnostics event row.</summary>
public sealed class DiagnosticsEventViewModel
{
    public string TimeText    { get; init; } = "";
    public string ServiceName { get; init; } = "";
    public string Message     { get; init; } = "";
    public string SeverityColor { get; init; } = "#6B7280";
    public string GlyphCode   { get; init; } = "";
    public Visibility HasDetail { get; init; }
    public string Detail      { get; init; } = "";

    internal DiagnosticsEventViewModel(DiagnosticsEvent e)
    {
        TimeText    = e.OccurredAt.LocalDateTime.ToString("HH:mm:ss");
        ServiceName = e.ServiceName;
        Message     = e.Message;
        Detail      = e.Detail ?? "";
        HasDetail   = !string.IsNullOrEmpty(e.Detail) ? Visibility.Visible : Visibility.Collapsed;

        SeverityColor = e.Severity switch
        {
            DiagnosticSeverity.Critical => "#EF4444",
            DiagnosticSeverity.Warning  => "#F59E0B",
            _                           => "#6B7280",
        };

        GlyphCode = e.Type switch
        {
            DiagnosticsEventType.ServiceStarted   or
            DiagnosticsEventType.ServiceRecovered or
            DiagnosticsEventType.RecoverySucceeded  => "",  // checkmark
            DiagnosticsEventType.SamplerFailure    or
            DiagnosticsEventType.ExecutorCrash     or
            DiagnosticsEventType.ServiceDegraded     => "",  // warning
            DiagnosticsEventType.ModeTransition      => "",  // settings
            DiagnosticsEventType.RecoveryAttempt     => "",  // refresh
            DiagnosticsEventType.MemoryPressure      => "",  // chart
            DiagnosticsEventType.PollingOverrun      => "",  // timer
            DiagnosticsEventType.QueuePressure or
            DiagnosticsEventType.PersistenceStall    => "",  // queue
            DiagnosticsEventType.DegradedModeEntered => "",  // down
            DiagnosticsEventType.DegradedModeExited  => "",  // up
            _                                        => "",  // info
        };
    }
}

// ── Main ViewModel ─────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for the Internal Diagnostics page.
///
/// Subscribes to <see cref="IInternalDiagnosticsService.EventAppended"/> for
/// live event streaming, and provides a Refresh command for on-demand updates.
///
/// All observable property writes happen on the UI thread:
///   - EventAppended fires on the reporting thread → we dispatch to UI via DispatcherQueue.
///   - Refresh/Initialize use await + ConfigureAwait(true) to stay on UI thread.
/// </summary>
public sealed partial class DiagnosticsViewModel : ObservableObject
{
    private readonly IInternalDiagnosticsService _diagnostics;

    // ── Observable properties ──────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingVisibility))]
    [NotifyPropertyChangedFor(nameof(ContentVisibility))]
    private bool _isLoading = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DegradedModeBannerVisibility))]
    [NotifyPropertyChangedFor(nameof(DegradedModeColor))]
    private string _degradedModeLabel = "Full";

    [ObservableProperty]
    private string _degradedModeDescription = "All subsystems operating normally.";

    [ObservableProperty]
    private string _processMemoryText  = "—";
    [ObservableProperty]
    private string _heapMemoryText     = "—";
    [ObservableProperty]
    private string _activeWorkloadsText = "—";
    [ObservableProperty]
    private string _queueDepthText     = "—";
    [ObservableProperty]
    private string _eventCountText     = "—";
    [ObservableProperty]
    private string _snapshotTimeText   = "";
    [ObservableProperty]
    private string _persistenceStatus  = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEventsVisibility))]
    [NotifyPropertyChangedFor(nameof(NoEventsVisibility))]
    private bool _hasEvents;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExecutorIssuesVisibility))]
    [NotifyPropertyChangedFor(nameof(NoExecutorIssuesVisibility))]
    private bool _hasExecutorIssues;

    // ── Derived Visibility ─────────────────────────────────────────────────────

    public Visibility LoadingVisibility           => IsLoading         ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ContentVisibility           => !IsLoading        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasEventsVisibility         => HasEvents         ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoEventsVisibility          => !HasEvents        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasExecutorIssuesVisibility => HasExecutorIssues ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoExecutorIssuesVisibility  => !HasExecutorIssues ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DegradedModeBannerVisibility =>
        DegradedModeLabel != "Full" ? Visibility.Visible : Visibility.Collapsed;

    public string DegradedModeColor => DegradedModeLabel switch
    {
        "Recovery Only"     => "#EF4444",
        "Minimal Monitoring" => "#F59E0B",
        "Reduced Telemetry" => "#F59E0B",
        _                   => "#22C55E",
    };

    // ── Collections ────────────────────────────────────────────────────────────

    public ObservableCollection<ServiceHealthCardViewModel>  ServiceCards     { get; } = [];
    public ObservableCollection<DiagnosticsEventViewModel>   RecentEvents     { get; } = [];
    public ObservableCollection<SamplerHealthEntry>          SamplerHealth    { get; } = [];
    public ObservableCollection<ExecutorFailureSummary>      ExecutorIssues   { get; } = [];
    public IReadOnlyList<RecoveryAction>                     RecoveryActions  { get; } =
        RecoveryCoordinator.AvailableActions;

    // ── Recovery result ────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _recoveryResultText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecoveryResultVisibility))]
    private bool _hasRecoveryResult;

    public Visibility RecoveryResultVisibility => HasRecoveryResult ? Visibility.Visible : Visibility.Collapsed;

    // ── Commands ───────────────────────────────────────────────────────────────

    public IAsyncRelayCommand RefreshCommand                          { get; }
    public IAsyncRelayCommand<string> ExecuteRecoveryActionCommand   { get; }

    // ── Dispatcher ────────────────────────────────────────────────────────────

    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _uiDispatcher;

    // ── Construction ──────────────────────────────────────────────────────────

    public DiagnosticsViewModel(IInternalDiagnosticsService diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        _diagnostics   = diagnostics;
        _uiDispatcher  = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        RefreshCommand               = new AsyncRelayCommand(RefreshAsync);
        ExecuteRecoveryActionCommand = new AsyncRelayCommand<string>(ExecuteRecoveryActionAsync);

        // Subscribe to live event stream.
        _diagnostics.EventAppended       += OnEventAppended;
        _diagnostics.DegradedModeChanged += OnDegradedModeChanged;
    }

    // ── Initialization ─────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        IsLoading = true;
        await RefreshAsync().ConfigureAwait(true);
        IsLoading = false;
    }

    // ── Refresh ────────────────────────────────────────────────────────────────

    private async Task RefreshAsync()
    {
        var (snapshot, health, samplers, executors, events) = await Task.Run(() =>
        {
            var s  = _diagnostics.GetSnapshot();
            var h  = _diagnostics.GetServiceHealth();
            var sa = _diagnostics.GetSamplerHealth();
            var ex = _diagnostics.GetExecutorSummaries();
            var ev = _diagnostics.GetRecentEvents(50);
            return (s, h, sa, ex, ev);
        }).ConfigureAwait(true);

        ApplySnapshot(snapshot);

        ServiceCards.Clear();
        foreach (var kvp in health.OrderBy(k => k.Key))
            ServiceCards.Add(new ServiceHealthCardViewModel(kvp.Value));

        SamplerHealth.Clear();
        foreach (var s in samplers)
            SamplerHealth.Add(s);

        ExecutorIssues.Clear();
        var concerning = executors.Where(e => e.TotalFailures > 0).ToList();
        foreach (var e in concerning)
            ExecutorIssues.Add(e);
        HasExecutorIssues = ExecutorIssues.Count > 0;

        RecentEvents.Clear();
        foreach (var ev in events)
            RecentEvents.Add(new DiagnosticsEventViewModel(ev));
        HasEvents = RecentEvents.Count > 0;
    }

    // ── Snapshot application ───────────────────────────────────────────────────

    private void ApplySnapshot(SelfTelemetrySnapshot s)
    {
        ProcessMemoryText  = $"{s.WorkingSetMb:F0} MB";
        HeapMemoryText     = $"{s.HeapMb:F0} MB";
        ActiveWorkloadsText = s.ActiveWorkloadCount.ToString();
        QueueDepthText     = s.PersistenceQueueDepth.ToString();
        EventCountText     = s.DiagnosticsEventCount.ToString();
        SnapshotTimeText   = $"Updated {s.SnapshotAt.LocalDateTime:h:mm:ss tt}";
        PersistenceStatus  = s.PersistenceIsHealthy ? "Healthy" : "Unhealthy";

        DegradedModeLabel = s.CurrentDegradedMode switch
        {
            DegradedMode.Full              => "Full",
            DegradedMode.ReducedTelemetry  => "Reduced Telemetry",
            DegradedMode.MinimalMonitoring => "Minimal Monitoring",
            DegradedMode.RecoveryOnly      => "Recovery Only",
            _                              => "Full",
        };
        DegradedModeDescription = s.CurrentDegradedMode switch
        {
            DegradedMode.ReducedTelemetry  => "One or more samplers are experiencing issues. Telemetry collection continues at reduced rate.",
            DegradedMode.MinimalMonitoring => "Multiple subsystems are degraded. Only critical-path telemetry is active.",
            DegradedMode.RecoveryOnly      => "Platform in recovery mode. Background analysis paused to allow stabilization.",
            _                              => "All subsystems operating normally.",
        };
    }

    // ── Recovery ───────────────────────────────────────────────────────────────

    private async Task ExecuteRecoveryActionAsync(string? actionId)
    {
        if (string.IsNullOrEmpty(actionId)) return;

        HasRecoveryResult  = false;
        RecoveryResultText = "";

        var result = await _diagnostics.Recovery.ExecuteAsync(actionId).ConfigureAwait(true);

        RecoveryResultText = result.Succeeded
            ? $"✓ {result.Message}"
            : $"✗ {result.Message}";
        HasRecoveryResult  = true;

        // Refresh the page to reflect any changes the recovery action made.
        await RefreshAsync().ConfigureAwait(true);
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    private void OnEventAppended(object? sender, DiagnosticsEvent ev)
    {
        // EventAppended fires on the reporting thread — dispatch to UI.
        _uiDispatcher?.TryEnqueue(() =>
        {
            // Insert newest-first
            RecentEvents.Insert(0, new DiagnosticsEventViewModel(ev));

            // Trim to 50 visible entries
            while (RecentEvents.Count > 50)
                RecentEvents.RemoveAt(RecentEvents.Count - 1);

            HasEvents = RecentEvents.Count > 0;
        });
    }

    private void OnDegradedModeChanged(object? sender, DegradedModeChangedEventArgs args)
    {
        // Already on UI thread (dispatched by InternalDiagnosticsService)
        DegradedModeLabel = args.NewMode switch
        {
            DegradedMode.Full              => "Full",
            DegradedMode.ReducedTelemetry  => "Reduced Telemetry",
            DegradedMode.MinimalMonitoring => "Minimal Monitoring",
            DegradedMode.RecoveryOnly      => "Recovery Only",
            _                              => "Full",
        };
        DegradedModeDescription = args.Description;
    }

    // ── Cleanup ────────────────────────────────────────────────────────────────

    public void Cleanup()
    {
        _diagnostics.EventAppended       -= OnEventAppended;
        _diagnostics.DegradedModeChanged -= OnDegradedModeChanged;
    }
}
