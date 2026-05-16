using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplainMyPC.Core.MVVM;
using ExplainMyPC.Core.Navigation;
using ExplainMyPC.Intelligence;
using ExplainMyPC.Intelligence.Models;
using ExplainMyPC.Models;
using ExplainMyPC.Services;
using Microsoft.UI.Dispatching;

namespace ExplainMyPC.ViewModels;

/// <summary>
/// ViewModel for the Dashboard page.
///
/// Architecture:
///   • Subscribes to ITelemetryService.ReadingUpdated on the ThreadPool.
///   • Marshals every reading to the UI thread via DispatcherQueue.
///   • Maintains 60-point rolling queues for graph history AND a 30-reading
///     history queue that feeds the Intelligence Engine for trend analysis.
///   • On every reading, calls IIntelligenceEngine.Analyze() synchronously.
///     Rules are fast in-memory comparisons; the ~1 ms cost is negligible.
///   • Exposes double[] snapshots (not ObservableCollection) so x:Bind fires
///     a full property-changed notification and TelemetryGraph re-renders.
///
/// Cleanup:
///   DashboardPage.Unloaded calls Cleanup() to unsubscribe from the singleton
///   ITelemetryService, preventing it from holding a reference to this
///   transient ViewModel after navigation.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private const int GraphHistoryLength   = 60;
    private const int EngineHistoryLength  = 30;

    private readonly INavigationService   _navigationService;
    private readonly ITelemetryService    _telemetryService;
    private readonly IIntelligenceEngine  _engine;
    private readonly DispatcherQueue      _dispatcher;

    // ── Rolling graph queues ──────────────────────────────────────────────────
    private readonly Queue<double>           _cpuQueue;
    private readonly Queue<double>           _ramQueue;
    private readonly Queue<double>           _gpuQueue;
    private readonly Queue<double>           _diskQueue;

    // ── Engine history queue (full readings for trend analysis) ───────────────
    private readonly Queue<TelemetryReading> _readingHistory;

    // ── Startup app count — updated by StartupScanner (Phase 2) ──────────────
    private int _startupAppCount;

    // ════════════════════════════════════════════════════════════════════════
    // Observable properties — all updated on the UI thread
    // ════════════════════════════════════════════════════════════════════════

    // ── Health scores ─────────────────────────────────────────────────────────
    [ObservableProperty] private int    _overallHealthScore;
    [ObservableProperty] private string _overallHealthLabel  = string.Empty;
    [ObservableProperty] private int    _securityScore;
    [ObservableProperty] private int    _stabilityScore;
    [ObservableProperty] private int    _storageScore;
    [ObservableProperty] private int    _performanceScore;
    [ObservableProperty] private int    _privacyScore;

    // ── Live metrics ──────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuDisplay))]
    [NotifyPropertyChangedFor(nameof(CpuDetail))]
    private double _cpuPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuDetail))]
    private double _cpuTemperature;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuDetail))]
    private double _cpuFrequencyGhz;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RamDisplay))]
    [NotifyPropertyChangedFor(nameof(RamDetail))]
    private double _ramPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RamDetail))]
    private long _ramUsedMb;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RamDetail))]
    private long _ramTotalMb;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpuDisplay))]
    [NotifyPropertyChangedFor(nameof(GpuDetail))]
    private double _gpuPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpuDetail))]
    private double _gpuTemperature;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpuDetail))]
    private double _gpuVramUsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskDisplay))]
    [NotifyPropertyChangedFor(nameof(DiskDetail))]
    private double _diskReadMbps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskDetail))]
    private double _diskWriteMbps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskDisplay))]
    private double _diskIoPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskDetail))]
    private long _diskUsedGb;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskDetail))]
    private long _diskTotalGb;

    // ── Graph history arrays ──────────────────────────────────────────────────
    [ObservableProperty] private double[] _cpuHistory;
    [ObservableProperty] private double[] _ramHistory;
    [ObservableProperty] private double[] _gpuHistory;
    [ObservableProperty] private double[] _diskHistory;

    // ── Intelligence output ───────────────────────────────────────────────────
    [ObservableProperty] private string _systemSummary =
        "Waiting for telemetry data…";
    [ObservableProperty] private int _activeIssueCount;
    [ObservableProperty] private IReadOnlyList<SystemIssue> _systemIssues = [];

    // ════════════════════════════════════════════════════════════════════════
    // Computed display strings
    // ════════════════════════════════════════════════════════════════════════

    public string CpuDisplay  => $"{CpuPercent:F0}%";
    public string CpuDetail   => $"{CpuTemperature:F0}°C  •  {CpuFrequencyGhz:F2} GHz";

    public string RamDisplay  => $"{RamPercent:F0}%";
    public string RamDetail   => $"{RamUsedMb / 1024.0:F1} GB / {RamTotalMb / 1024.0:F0} GB";

    public string GpuDisplay  => $"{GpuPercent:F0}%";
    public string GpuDetail   => $"{GpuTemperature:F0}°C  •  {GpuVramUsed:F1} GB VRAM";

    public string DiskDisplay => $"↑{DiskReadMbps:F0}  ↓{DiskWriteMbps:F0} MB/s";
    public string DiskDetail  => $"{DiskUsedGb} GB / {DiskTotalGb} GB used";

    // ════════════════════════════════════════════════════════════════════════
    // Constructor
    // ════════════════════════════════════════════════════════════════════════

    public DashboardViewModel(
        INavigationService   navigationService,
        ITelemetryService    telemetryService,
        IIntelligenceEngine  intelligenceEngine)
    {
        _navigationService = navigationService;
        _telemetryService  = telemetryService;
        _engine            = intelligenceEngine;
        _dispatcher        = DispatcherQueue.GetForCurrentThread();

        // Seed graph queues with a flat baseline so the graphs render immediately.
        _cpuQueue  = new Queue<double>(Enumerable.Repeat(0.0, GraphHistoryLength));
        _ramQueue  = new Queue<double>(Enumerable.Repeat(0.0, GraphHistoryLength));
        _gpuQueue  = new Queue<double>(Enumerable.Repeat(0.0, GraphHistoryLength));
        _diskQueue = new Queue<double>(Enumerable.Repeat(0.0, GraphHistoryLength));

        _cpuHistory  = [.. _cpuQueue];
        _ramHistory  = [.. _ramQueue];
        _gpuHistory  = [.. _gpuQueue];
        _diskHistory = [.. _diskQueue];

        _readingHistory = new Queue<TelemetryReading>(EngineHistoryLength);

        // Wire up telemetry.
        _telemetryService.ReadingUpdated += OnReadingUpdated;
        _telemetryService.Start();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Commands
    // ════════════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void OpenExplain() => _navigationService.Navigate("explain");

    [RelayCommand]
    private void RunScan()
    {
        // Phase 2: trigger ScanService.RunFullScanAsync().
        StatusMessage = "Full scan available in Phase 2.";
    }

    // ════════════════════════════════════════════════════════════════════════
    // Telemetry intake
    // ════════════════════════════════════════════════════════════════════════

    private void OnReadingUpdated(object? sender, TelemetryReading r)
    {
        _dispatcher.TryEnqueue(() => ApplyReading(r));
    }

    private void ApplyReading(TelemetryReading r)
    {
        // ── Raw metric values ─────────────────────────────────────────────
        CpuPercent      = r.CpuPercent;
        CpuTemperature  = r.CpuTemperatureCelsius;
        CpuFrequencyGhz = r.CpuFrequencyGhz;

        RamPercent      = r.RamPercent;
        RamUsedMb       = r.RamUsedMb;
        RamTotalMb      = r.RamTotalMb;

        GpuPercent      = r.GpuPercent;
        GpuTemperature  = r.GpuTemperatureCelsius;
        GpuVramUsed     = r.GpuVramUsedGb;

        DiskReadMbps    = r.DiskReadMbps;
        DiskWriteMbps   = r.DiskWriteMbps;
        DiskIoPercent   = r.DiskUsagePercent;
        DiskUsedGb      = r.DiskUsedGb;
        DiskTotalGb     = r.DiskTotalGb;

        // ── Roll graph queues ─────────────────────────────────────────────
        EnqueueGraph(_cpuQueue,  r.CpuPercent);
        EnqueueGraph(_ramQueue,  r.RamPercent);
        EnqueueGraph(_gpuQueue,  r.GpuPercent);
        EnqueueGraph(_diskQueue, r.DiskUsagePercent);

        CpuHistory  = [.. _cpuQueue];
        RamHistory  = [.. _ramQueue];
        GpuHistory  = [.. _gpuQueue];
        DiskHistory = [.. _diskQueue];

        // ── Roll engine history ───────────────────────────────────────────
        if (_readingHistory.Count >= EngineHistoryLength)
            _readingHistory.Dequeue();
        _readingHistory.Enqueue(r);

        // ── Run intelligence engine ───────────────────────────────────────
        var snapshot = new SystemSnapshot(
            Current:        r,
            RecentHistory:  [.. _readingHistory],
            StartupAppCount: _startupAppCount);

        var output = _engine.Analyze(snapshot);

        // ── Apply engine output ───────────────────────────────────────────
        OverallHealthScore  = output.Health.Overall;
        OverallHealthLabel  = output.Health.Label;
        SecurityScore       = output.Health.Security;
        StabilityScore      = output.Health.Stability;
        StorageScore        = output.Health.Storage;
        PerformanceScore    = output.Health.Performance;
        PrivacyScore        = output.Health.Privacy;

        SystemSummary    = output.SystemSummary;
        SystemIssues     = output.Issues;
        ActiveIssueCount = output.Issues.Count(i => i.Severity >= IssueSeverity.Low);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════════

    private static void EnqueueGraph(Queue<double> queue, double value)
    {
        if (queue.Count >= GraphHistoryLength) queue.Dequeue();
        queue.Enqueue(value);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Cleanup — called by DashboardPage.Unloaded
    // ════════════════════════════════════════════════════════════════════════

    public void Cleanup()
    {
        _telemetryService.ReadingUpdated -= OnReadingUpdated;
    }
}
