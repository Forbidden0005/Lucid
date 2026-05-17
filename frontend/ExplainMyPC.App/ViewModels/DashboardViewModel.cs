using CommunityToolkit.Mvvm.ComponentModel;

namespace ExplainMyPC.ViewModels;

/// <summary>
/// ViewModel for DashboardPage — Phase 1 (mock data, no services).
///
/// Property names and structure are designed so upgrading to live telemetry
/// later requires only adding a constructor parameter and hooking a service —
/// not renaming bindings or touching the XAML.
///
/// Live telemetry wiring happens in Phase 2 when ITelemetryService is introduced.
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    // ── Health Score ──────────────────────────────────────────────────────

    /// <summary>Overall health score 0–100. Typed as double for ProgressRing.Value.</summary>
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

    /// <summary>Week-over-week score change. Positive values display as "+3".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrendLabel))]
    private int _trendDelta = 3;

    public string TrendLabel => TrendDelta >= 0 ? $"+{TrendDelta}" : $"{TrendDelta}";

    // ── Sub-scores ────────────────────────────────────────────────────────

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

    // ── CPU ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuDisplay))]
    [NotifyPropertyChangedFor(nameof(CpuDetail))]
    private double _cpuPercent = 23;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuDetail))]
    private double _cpuTemperature = 42;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuDetail))]
    private double _cpuFrequencyGhz = 3.80;

    public string CpuDisplay => $"{CpuPercent:0}%";
    public string CpuDetail  => $"{CpuTemperature:0}°C  •  {CpuFrequencyGhz:F2} GHz";

    // ── RAM ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RamDisplay))]
    [NotifyPropertyChangedFor(nameof(RamDetail))]
    private double _ramPercent = 61;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RamDetail))]
    private double _ramUsedGb = 9.8;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RamDetail))]
    private double _ramTotalGb = 16;

    public string RamDisplay => $"{RamPercent:0}%";
    public string RamDetail  => $"{RamUsedGb:F1} GB / {RamTotalGb:F0} GB";

    // ── GPU ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpuDisplay))]
    [NotifyPropertyChangedFor(nameof(GpuDetail))]
    private double _gpuPercent = 12;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpuDetail))]
    private double _gpuTemperature = 38;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpuDetail))]
    private double _gpuVramUsedGb = 1.2;

    public string GpuDisplay => $"{GpuPercent:0}%";
    public string GpuDetail  => $"{GpuTemperature:0}°C  •  {GpuVramUsedGb:F1} GB VRAM";

    // ── Disk ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskDisplay))]
    [NotifyPropertyChangedFor(nameof(DiskDetail))]
    private double _diskPercent = 46;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskDetail))]
    private double _diskUsedGb = 237;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskDetail))]
    private double _diskTotalGb = 512;

    public string DiskDisplay => $"{DiskPercent:0}%";
    public string DiskDetail  => $"{DiskUsedGb:F0} GB / {DiskTotalGb:F0} GB";

    // ── Trends ────────────────────────────────────────────────────────────

    [ObservableProperty] private string _bootTimeValue = "18s";
    [ObservableProperty] private string _bootTimeDelta = "↓ 2s faster";

    [ObservableProperty] private string _cpuAvgValue = "31%";
    [ObservableProperty] private string _cpuAvgDelta = "↑ 4% higher";

    [ObservableProperty] private string _diskFreeValue = "275 GB";
    [ObservableProperty] private string _diskFreeDelta = "↓ 12 GB lost";

    // ── Insights ──────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InsightCountText))]
    private int _insightCount = 3;

    public string InsightCountText => $"{InsightCount} finding{(InsightCount == 1 ? "" : "s")}";
}
