using CommunityToolkit.Mvvm.ComponentModel;
using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Intelligence;

namespace ExplainMyPC.ViewModels;

/// <summary>
/// ViewModel for DashboardPage — Phase 3 (application service layer).
///
/// Subscribes to AppServices.Telemetry.ReadingAvailable so live CPU, RAM,
/// GPU, Disk, and thermal data update every ~1.5 s without managing a
/// TelemetryPoller directly. The service is app-level and outlives any
/// single page — Cleanup() only unsubscribes rather than stopping it.
///
/// Back-navigation:
///   The constructor reads AppServices.Telemetry.LastReading immediately so
///   the UI shows the most recent values the moment the page appears, rather
///   than waiting up to 1.5 s for the next poll tick.
///
/// Health scores, weekly trends, and insight cards remain mock data until
/// Phase 4 introduces the intelligence engine and scan history.
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    // ── Health Score (Phase 3: mock) ──────────────────────────────────────────

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

    // ── Sub-scores (Phase 3: mock) ────────────────────────────────────────────

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

    // ── CPU — real data ───────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuDisplay))]
    private double _cpuPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuDetail))]
    private double _cpuFrequencyGhz;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuDetail))]
    private int _cpuCoreCount = Environment.ProcessorCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuDetail))]
    private double _cpuTemperatureCelsius;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuDetail))]
    private bool _cpuTemperatureAvailable;

    public string CpuDisplay => $"{CpuPercent:0}%";

    /// <summary>
    /// Shows frequency, core count, and temperature (when available).
    /// Temperature is read from ACPI thermal zones via ThermalSampler —
    /// no kernel driver required, but may report N/A on VMs or certain hardware.
    /// </summary>
    public string CpuDetail
    {
        get
        {
            string freq = CpuFrequencyGhz > 0
                ? $"{CpuFrequencyGhz:F2} GHz  •  {CpuCoreCount} cores"
                : $"{CpuCoreCount} cores";
            return CpuTemperatureAvailable
                ? $"{freq}  •  {CpuTemperatureCelsius:0}°C"
                : freq;
        }
    }

    // ── RAM — real data ───────────────────────────────────────────────────────

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

    // ── GPU — real data ───────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpuDisplay))]
    private double _gpuPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpuDisplay))]
    [NotifyPropertyChangedFor(nameof(GpuDetail))]
    private bool _gpuAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpuDetail))]
    private double _gpuVramUsedGb;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpuDetail))]
    private double _gpuVramTotalGb;

    public string GpuDisplay => GpuAvailable ? $"{GpuPercent:0}%" : "—";

    /// <summary>
    /// Shows VRAM usage when the GPU Adapter Memory counter is available,
    /// or falls back to a generic label indicating 3D engine presence.
    /// </summary>
    public string GpuDetail => GpuAvailable
        ? (GpuVramTotalGb > 0
            ? $"{GpuVramUsedGb:F1} / {GpuVramTotalGb:F0} GB VRAM"
            : "3D Engine")
        : "Monitoring N/A";

    // ── Disk — real data ──────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskDisplay))]
    private double _diskPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskDetail))]
    private double _diskUsedGb;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskDetail))]
    private double _diskTotalGb;

    /// <summary>
    /// Disk I/O throughput — available for future UI panels (not shown on
    /// the current dashboard card, which focuses on storage space).
    /// </summary>
    [ObservableProperty] private double _diskReadMbps;
    [ObservableProperty] private double _diskWriteMbps;

    public string DiskDisplay => DiskPercent > 0 ? $"{DiskPercent:0}%" : "—";
    public string DiskDetail  => DiskTotalGb > 0 ? $"{DiskUsedGb:F0} GB / {DiskTotalGb:F0} GB" : "Loading…";

    // ── Trends (Phase 3: mock) ────────────────────────────────────────────────

    [ObservableProperty] private string _bootTimeValue = "18s";
    [ObservableProperty] private string _bootTimeDelta = "↓ 2s faster";
    [ObservableProperty] private string _cpuAvgValue   = "31%";
    [ObservableProperty] private string _cpuAvgDelta   = "↑ 4% higher";
    [ObservableProperty] private string _diskFreeValue = "275 GB";
    [ObservableProperty] private string _diskFreeDelta = "↓ 12 GB lost";

    // ── Insights — live from SystemInsightEngine ──────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InsightCountText))]
    private int _insightCount = 0;

    public string InsightCountText => $"{InsightCount} finding{(InsightCount == 1 ? "" : "s")}";

    /// <summary>
    /// The current list of intelligence findings, ordered highest severity
    /// first. Updated whenever the engine detects a change in the active set.
    /// </summary>
    public IReadOnlyList<SystemInsight> CurrentInsights { get; private set; } = [];

    /// <summary>
    /// Findings grouped by severity tier (Warnings → Recommendations → Informational),
    /// each group pre-sorted for display. Only non-empty groups are included.
    /// Bind the Active Insights section's ItemsRepeater to this property.
    /// </summary>
    public IReadOnlyList<InsightGroupViewModel> InsightGroups { get; private set; } = [];

    // Card cache: preserves IsExpanded state across engine re-evaluations.
    private readonly Dictionary<string, InsightCardViewModel> _cardCache = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    public DashboardViewModel()
    {
        // Subscribe to the app-level telemetry service.
        AppServices.Telemetry.ReadingAvailable += OnReadingAvailable;

        // If a reading already exists (back-navigation or late construction),
        // apply it immediately so the UI doesn't wait for the next poll cycle.
        if (AppServices.Telemetry.LastReading is { } snapshot)
            OnReadingAvailable(null, snapshot);

        // Subscribe to intelligence findings.
        AppServices.Intelligence.InsightsUpdated += OnInsightsUpdated;

        // Seed from whatever the engine has already evaluated (back-navigation).
        ApplyInsights(AppServices.Intelligence.CurrentInsights);
    }

    // ── Telemetry intake ──────────────────────────────────────────────────────

    /// <summary>
    /// Called on the UI thread by ITelemetryService via DispatcherQueue.
    /// Safe to set ObservableObject properties directly — no marshalling needed.
    /// </summary>
    private void OnReadingAvailable(object? sender, TelemetrySnapshot s)
    {
        CpuPercent              = s.CpuPercent;
        CpuFrequencyGhz         = s.CpuFrequencyGhz;
        CpuCoreCount            = s.CpuCoreCount;
        CpuTemperatureCelsius   = s.CpuTemperatureCelsius;
        CpuTemperatureAvailable = s.CpuTemperatureAvailable;

        RamPercent = s.RamPercent;
        RamUsedGb  = s.RamUsedGb;
        RamTotalGb = s.RamTotalGb;

        GpuPercent     = s.GpuPercent;
        GpuAvailable   = s.GpuAvailable;
        GpuVramUsedGb  = s.GpuVramUsedGb;
        GpuVramTotalGb = s.GpuVramTotalGb;

        DiskPercent   = s.DiskPercent;
        DiskUsedGb    = s.DiskUsedGb;
        DiskTotalGb   = s.DiskTotalGb;
        DiskReadMbps  = s.DiskReadMbps;
        DiskWriteMbps = s.DiskWriteMbps;
    }

    // ── Intelligence intake ───────────────────────────────────────────────────

    /// <summary>
    /// Called on the UI thread whenever the active set of findings changes.
    /// Fires much less frequently than telemetry ticks (only on set changes).
    /// </summary>
    private void OnInsightsUpdated(object? sender, IReadOnlyList<SystemInsight> insights) =>
        ApplyInsights(insights);

    private void ApplyInsights(IReadOnlyList<SystemInsight> insights)
    {
        CurrentInsights = insights;
        InsightCount    = insights.Count;
        OnPropertyChanged(nameof(CurrentInsights));

        // ── Update card cache ─────────────────────────────────────────────────
        // Remove cards whose findings have cleared.
        var activeIds = new HashSet<string>(insights.Select(i => i.Id));
        foreach (var stale in _cardCache.Keys.Where(k => !activeIds.Contains(k)).ToList())
            _cardCache.Remove(stale);

        // Add new cards; refresh existing ones (preserves IsExpanded state).
        foreach (var insight in insights)
        {
            if (_cardCache.TryGetValue(insight.Id, out var existing))
                existing.Update(insight);
            else
                _cardCache[insight.Id] = new InsightCardViewModel(insight);
        }

        // ── Rebuild ordered groups ────────────────────────────────────────────
        InsightGroups = BuildGroups();
        OnPropertyChanged(nameof(InsightGroups));
    }

    private IReadOnlyList<InsightGroupViewModel> BuildGroups()
    {
        var groups = new List<InsightGroupViewModel>(3)
        {
            MakeGroup(InsightSeverity.Warning,        "Warnings",       ""),
            MakeGroup(InsightSeverity.Recommendation, "Recommendations", ""),
            MakeGroup(InsightSeverity.Info,           "Informational",  ""),
        };
        return groups.Where(g => g.HasItems).ToList();
    }

    private InsightGroupViewModel MakeGroup(InsightSeverity severity, string title, string icon)
    {
        var cards = _cardCache.Values
            .Where(c => c.Severity == severity)
            .OrderBy(c => c.SortKey)
            .ToList();
        return new InsightGroupViewModel(severity, title, icon, cards);
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Unsubscribes from app-level services.
    /// Does NOT stop them — they are app-level and may serve other pages.
    /// Call from DashboardPage.Unloaded.
    /// </summary>
    public void Cleanup()
    {
        AppServices.Telemetry.ReadingAvailable   -= OnReadingAvailable;
        AppServices.Intelligence.InsightsUpdated -= OnInsightsUpdated;
    }
}
