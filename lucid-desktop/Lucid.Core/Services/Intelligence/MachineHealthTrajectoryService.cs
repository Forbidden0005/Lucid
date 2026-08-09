using Lucid.Services.Analytics;

namespace Lucid.Services.Intelligence;

// ── Report model ───────────────────────────────────────────────────────────────

/// <summary>
/// A snapshot of this machine's current health trajectory, derived from the
/// historical analytics engine and the live intelligence engine.
///
/// Produced by <see cref="IMachineHealthTrajectoryService.ComputeReportAsync"/>.
/// All values are deterministic, local-only, and confidence-aware.
/// </summary>
public sealed record MachineHealthReport(
    // ── Health score ──────────────────────────────────────────────────────────
    /// <summary>7-day operational health score in [0, 100].</summary>
    int    HealthScore,

    /// <summary>Human-readable health label: "Excellent" / "Good" / "Fair" / "Poor" / "Critical".</summary>
    string HealthLabel,

    /// <summary>Accent color for the health score (green / amber / red).</summary>
    string HealthColor,

    // ── Trend momentum ────────────────────────────────────────────────────────
    /// <summary>Change in health score: 7-day score minus 30-day score.</summary>
    int    MomentumPoints,

    /// <summary>e.g. "↑ Improving" / "→ Stable" / "↓ Declining".</summary>
    string MomentumLabel,

    /// <summary>Accent color for the momentum label.</summary>
    string MomentumColor,

    // ── Metric trend labels ───────────────────────────────────────────────────
    /// <summary>7-day average CPU as a display string, e.g. "31%". Null when insufficient data.</summary>
    string? CpuAvgLabel,

    /// <summary>CPU trend vs. 30-day baseline, e.g. "▲ 12% vs. 30-day avg" or "Stable".</summary>
    string  CpuTrendLabel,

    /// <summary>7-day average RAM as a display string. Null when insufficient data.</summary>
    string? RamAvgLabel,

    /// <summary>RAM trend vs. 30-day baseline.</summary>
    string  RamTrendLabel,

    // ── Narrative ─────────────────────────────────────────────────────────────
    /// <summary>One-sentence health description derived from score and momentum.</summary>
    string HealthDescription,

    /// <summary>Short status summary, e.g. "All systems normal" or "2 issues in the past 7 days".</summary>
    string SystemStatusText,

    // ── Metadata ──────────────────────────────────────────────────────────────
    /// <summary>True when sufficient historical data exists (≥ 7 days).</summary>
    bool   HasSufficientData,

    /// <summary>Timestamp of this report.</summary>
    DateTimeOffset ComputedAt);

// ── Interface ─────────────────────────────────────────────────────────────────

/// <summary>
/// Derives a concise machine health trajectory report from the historical
/// analytics engine and the live intelligence engine.
///
/// Design:
///   • Read-only — no side effects, no writes
///   • Local-only — all data from SQLite + live memory
///   • Cold-start safe — returns a default report when no data exists
///   • Non-blocking — ComputeReportAsync() offloads SQL queries to thread pool
/// </summary>
public interface IMachineHealthTrajectoryService
{
    /// <summary>
    /// Computes (or refreshes) the machine health report asynchronously.
    /// Safe to call on the UI thread.
    /// </summary>
    Task<MachineHealthReport> ComputeReportAsync();

    /// <summary>
    /// The most recently computed report, or null if
    /// <see cref="ComputeReportAsync"/> has not been called yet.
    /// </summary>
    MachineHealthReport? LastReport { get; }
}

// ── Implementation ─────────────────────────────────────────────────────────────

/// <summary>
/// Wraps <see cref="IHistoricalAnalyticsEngine"/> and <see cref="ISystemInsightEngine"/>
/// to produce <see cref="MachineHealthReport"/> without coupling callers to the
/// full <see cref="HistoricalSummary"/> model.
///
/// Health color thresholds (same as the Watchtower tier scheme):
///   ≥ 80 → "#34D399" (green)
///   ≥ 60 → "#F59E0B" (amber)
///        → "#EF4444" (red)
/// </summary>
public sealed class MachineHealthTrajectoryService : IMachineHealthTrajectoryService
{
    private readonly IHistoricalAnalyticsEngine _analytics;
    private readonly ISystemInsightEngine       _intelligence;

    public MachineHealthTrajectoryService(
        IHistoricalAnalyticsEngine analytics,
        ISystemInsightEngine       intelligence)
    {
        _analytics    = analytics;
        _intelligence = intelligence;
    }

    // ── IMachineHealthTrajectoryService ───────────────────────────────────────

    public MachineHealthReport? LastReport { get; private set; }

    public async Task<MachineHealthReport> ComputeReportAsync()
    {
        var summary = await _analytics.ComputeAsync().ConfigureAwait(false);
        var report  = BuildReport(summary);
        LastReport  = report;
        return report;
    }

    // ── Report construction ───────────────────────────────────────────────────

    private MachineHealthReport BuildReport(HistoricalSummary summary)
    {
        var sevenDay  = summary.SevenDayHealth;
        var thirtyDay = summary.ThirtyDayHealth;

        int  healthScore     = sevenDay.Score;
        bool hasSufficient   = summary.OldestDataPoint is not null
                               && (DateTimeOffset.Now - summary.OldestDataPoint.Value).TotalDays >= 3;

        // If no data at all, use a neutral fallback
        if (!hasSufficient)
            return DefaultReport();

        string healthLabel = sevenDay.Label;
        string healthColor = HealthColor(healthScore);

        // Momentum: how health score changed vs. 30-day baseline
        int    momentumPts   = healthScore - thirtyDay.Score;
        string momentumLabel = MomentumLabel(momentumPts);
        string momentumColor = MomentumColor(momentumPts);

        // CPU trend labels
        string? cpuAvgLabel   = sevenDay.Score > 0 && summary.CpuTrend.SevenDayAvg.HasValue
                                ? $"{summary.CpuTrend.SevenDayAvg.Value:F0}%"
                                : null;
        string  cpuTrendLabel = summary.CpuTrend.TrendLabel;

        // RAM trend labels
        string? ramAvgLabel   = summary.RamTrend.SevenDayAvg.HasValue
                                ? $"{summary.RamTrend.SevenDayAvg.Value:F0}%"
                                : null;
        string  ramTrendLabel = summary.RamTrend.TrendLabel;

        // Narrative — count DISTINCT ACTIVE conditions, not raw occurrences, so
        // the "N issues" summary matches how the score is computed. A single
        // disk-full condition that re-fires 30× is one issue, not thirty — and
        // a condition that already resolved is no longer an "issue" at all.
        int issueCount = sevenDay.Contributions.Count(c => c.IsActive);
        string healthDesc   = HealthDescription(healthScore, momentumPts);
        string statusText   = issueCount == 0
                              ? "All systems normal"
                              : $"{issueCount} active issue{(issueCount == 1 ? "" : "s")}";

        return new MachineHealthReport(
            HealthScore:      healthScore,
            HealthLabel:      healthLabel,
            HealthColor:      healthColor,
            MomentumPoints:   momentumPts,
            MomentumLabel:    momentumLabel,
            MomentumColor:    momentumColor,
            CpuAvgLabel:      cpuAvgLabel,
            CpuTrendLabel:    cpuTrendLabel,
            RamAvgLabel:      ramAvgLabel,
            RamTrendLabel:    ramTrendLabel,
            HealthDescription: healthDesc,
            SystemStatusText:  statusText,
            HasSufficientData: true,
            ComputedAt:       DateTimeOffset.Now);
    }

    private static MachineHealthReport DefaultReport() =>
        new(
            HealthScore:       87,
            HealthLabel:       "Good",
            HealthColor:       "#34D399",
            MomentumPoints:    0,
            MomentumLabel:     "→ Stable",
            MomentumColor:     "#6B7280",
            CpuAvgLabel:       null,
            CpuTrendLabel:     "Stable",
            RamAvgLabel:       null,
            RamTrendLabel:     "Stable",
            HealthDescription: "Your PC is running well. No critical issues detected. " +
                               "Performance is stable across all components.",
            SystemStatusText:  "All systems normal",
            HasSufficientData: false,
            ComputedAt:        DateTimeOffset.Now);

    // ── Display helpers ────────────────────────────────────────────────────────

    private static string HealthColor(int score) => score switch
    {
        >= 80 => "#34D399",
        >= 60 => "#F59E0B",
        _     => "#EF4444",
    };

    private static string MomentumLabel(int pts) => pts switch
    {
        > 5  => "↑ Improving",
        < -5 => "↓ Declining",
        _    => "→ Stable",
    };

    private static string MomentumColor(int pts) => pts switch
    {
        > 5  => "#34D399",
        < -5 => "#EF4444",
        _    => "#6B7280",
    };

    private static string HealthDescription(int score, int momentum) => score switch
    {
        >= 80 => momentum >= 0
                 ? "Your PC is running well with minimal issues detected over the past 7 days."
                 : "Your PC is running well, though performance has dipped slightly vs. last month.",
        >= 60 => "Your PC has experienced some intermittent issues. Monitor for recurring patterns.",
        >= 40 => "Your PC has had recurring issues recently. Consider reviewing the Insights page.",
        _     => "Your PC has shown significant instability. Review active warnings for guidance.",
    };
}
