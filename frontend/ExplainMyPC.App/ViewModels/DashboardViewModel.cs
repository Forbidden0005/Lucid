using CommunityToolkit.Mvvm.ComponentModel;
using ExplainMyPC.Helpers;
using Microsoft.UI.Dispatching;

namespace ExplainMyPC.ViewModels;

/// <summary>
/// ViewModel for DashboardPage — Phase 2 (real hardware telemetry).
///
/// Live metrics (CPU %, frequency, RAM, disk, GPU) are updated by
/// <see cref="TelemetryPoller"/> every ~1.5 s on the UI thread via
/// DispatcherQueue, so properties can be set directly without additional
/// marshalling in <see cref="OnReadingAvailable"/>.
///
/// Health scores, weekly trends, and insight cards remain mock data until
/// Phase 3 introduces the intelligence engine and scan history.
///
/// Lifetime constraint:
///   This class must be constructed on the UI thread because the
///   TelemetryPoller captures <c>DispatcherQueue.GetForCurrentThread()</c>
///   in its constructor. Call <see cref="Cleanup"/> from
///   <c>DashboardPage.Unloaded</c> to stop the background poller.
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    private readonly TelemetryPoller _poller;

    // ── Health Score (Phase 2: mock — replaced by intelligence engine in Phase 3) ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HealthScoreText))]
    private double _healthScore = 87;

    public string HealthScoreText => $"{HealthScore:0}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HealthTitle))]
    private string _healthLabel = "Good";

    public string HealthTitle => $"System Health: {HealthLabel}";

    [ObservableProperty]
    private string _healthDescription =
        "Your PC is running well. No critical issues detected. " +
        "Performance is stable across all components.";

    [ObservableProperty]
    private string _systemStatusText = "All systems normal";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrendLabel))]
    private int _trendDelta = 3;

    public string TrendLabel => TrendDelta >= 0 ? $"+{TrendDelta}" : $"{TrendDelta}";

    // ── Sub-scores (Phase 2: mock) ────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PerformanceScoreText))]
    private int _performanceScore = 92;

    public string PerformanceScoreText => $"Performance {PerformanceScore}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SecurityScoreText))]
    private int _securityScore = 95;

    public string SecurityScoreText => $"Security {SecurityScore}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StorageScoreText))]
    private int _storageScore = 71;

    public string StorageScoreText => $"Storage {StorageScore}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrivacyScoreText))]
    private int _privacyScore = 88;

    public string PrivacyScoreText => $"Privacy {PrivacyScore}";

    // ── CPU — real data ───────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuDisplay))]
    private double _cpuPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuDetail))]
    private double _cpuFrequencyGhz;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuDetail))]
    private int _cpuCoreCount = Environment.ProcessorCount;

    public string CpuDisplay => $"{CpuPercent:0}%";

    /// <summary>
    /// Shows real clock speed and logical-core count.
    /// CPU temperature requires hardware-sensor integration (Phase 3).
    /// </summary>
    public string CpuDetail => CpuFrequencyGhz > 0
        ? $"{CpuFrequencyGhz:F2} GHz  •  {CpuCoreCount} cores"
        : $"{CpuCoreCount} cores";

    // ── RAM — real data ───────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RamDisplay))]
    private double _ramPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RamDetail))]
    private double _ramUsedGb;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RamDetail))]
    private double _ramTotalGb;

    public string RamDisplay => RamPercent > 0 ? $"{RamPercent:0}%" : "—";
    public string RamDetail  => RamTotalGb > 0 ? $"{RamUsedGb:F1} GB / {RamTotalGb:F0} GB" : "Loading…";

    // ── GPU — real data when available, graceful N/A otherwise ───────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpuDisplay))]
    private double _gpuPercent;

    /// <summary>True once TelemetryPoller confirms a GPU Engine counter exists.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpuDisplay))]
    [NotifyPropertyChangedFor(nameof(GpuDetail))]
    private bool _gpuAvailable;

    public string GpuDisplay => GpuAvailable ? $"{GpuPercent:0}%" : "—";

    /// <summary>
    /// GPU VRAM usage and temperature require DirectX / hardware-sensor
    /// integration (Phase 3). For now we report engine availability only.
    /// </summary>
    public string GpuDetail  => GpuAvailable ? "3D Engine" : "Monitoring N/A";

    // ── Disk — real data ──────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskDisplay))]
    private double _diskPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskDetail))]
    private double _diskUsedGb;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskDetail))]
    private double _diskTotalGb;

    public string DiskDisplay => DiskPercent > 0 ? $"{DiskPercent:0}%" : "—";
    public string DiskDetail  => DiskTotalGb > 0 ? $"{DiskUsedGb:F0} GB / {DiskTotalGb:F0} GB" : "Loading…";

    // ── Trends (Phase 2: mock) ────────────────────────────────────────────

    [ObservableProperty] private string _bootTimeValue = "18s";
    [ObservableProperty] private string _bootTimeDelta = "↓ 2s faster";
    [ObservableProperty] private string _cpuAvgValue   = "31%";
    [ObservableProperty] private string _cpuAvgDelta   = "↑ 4% higher";
    [ObservableProperty] private string _diskFreeValue = "275 GB";
    [ObservableProperty] private string _diskFreeDelta = "↓ 12 GB lost";

    // ── Insights (Phase 2: mock) ──────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InsightCountText))]
    private int _insightCount = 3;

    public string InsightCountText => $"{InsightCount} finding{(InsightCount == 1 ? "" : "s")}";

    // ── Constructor ───────────────────────────────────────────────────────

    public DashboardViewModel()
    {
        // Must run on the UI thread — TelemetryPoller captures the dispatcher here.
        _poller = new TelemetryPoller(DispatcherQueue.GetForCurrentThread());
        _poller.ReadingAvailable += OnReadingAvailable;
        _poller.Start();
    }

    // ── Telemetry intake ──────────────────────────────────────────────────

    /// <summary>
    /// Called on the UI thread by TelemetryPoller via DispatcherQueue.
    /// Safe to set properties directly — no additional dispatching needed.
    /// </summary>
    private void OnReadingAvailable(object? sender, TelemetrySnapshot s)
    {
        CpuPercent      = s.CpuPercent;
        CpuFrequencyGhz = s.CpuFrequencyGhz;
        CpuCoreCount    = s.CpuCoreCount;

        RamPercent = s.RamPercent;
        RamUsedGb  = s.RamUsedGb;
        RamTotalGb = s.RamTotalGb;

        GpuPercent   = s.GpuPercent;
        GpuAvailable = s.GpuAvailable;

        DiskPercent = s.DiskPercent;
        DiskUsedGb  = s.DiskUsedGb;
        DiskTotalGb = s.DiskTotalGb;
    }

    // ── Cleanup ───────────────────────────────────────────────────────────

    /// <summary>
    /// Stops the background poller and releases performance counter handles.
    /// Call this from <c>DashboardPage.Unloaded</c>.
    /// </summary>
    public void Cleanup()
    {
        _poller.ReadingAvailable -= OnReadingAvailable;
        _poller.Dispose();
    }
}
