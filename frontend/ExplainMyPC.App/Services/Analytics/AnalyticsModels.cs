namespace ExplainMyPC.Services.Analytics;

// ── Trend direction ────────────────────────────────────────────────────────────

/// <summary>
/// Overall directionality of a computed trend.
/// Positive = getting worse (for metrics where lower is better).
/// </summary>
public enum TrendDirection
{
    Improving = -1,
    Stable    =  0,
    Worsening =  1,
}

// ── Long-term metric trend ─────────────────────────────────────────────────────

/// <summary>
/// Average and trend for a single hardware metric (CPU, RAM, or Disk)
/// computed across two look-back windows.
/// </summary>
public sealed record LongTermMetricTrend(
    /// <summary>Metric name for display ("CPU", "RAM", "Disk").</summary>
    string         MetricName,

    /// <summary>Mean over the past 7 days (null when insufficient data).</summary>
    double?        SevenDayAvg,

    /// <summary>Mean over the past 30 days (null when insufficient data).</summary>
    double?        ThirtyDayAvg,

    /// <summary>Trend direction comparing 7-day to 30-day.</summary>
    TrendDirection Direction,

    /// <summary>
    /// Percentage change from 30-day avg to 7-day avg.
    /// Positive = increased (worsened for CPU/RAM/Disk).
    /// Zero when 30-day data is unavailable.
    /// </summary>
    double         ChangePercent,

    /// <summary>Short human-readable text: "▲ 12% vs. 30-day avg", "Stable", "▼ 8%"</summary>
    string         TrendLabel);

// ── Health score ───────────────────────────────────────────────────────────────

/// <summary>
/// Operational health score for a rolling time window.
/// Derived from insight occurrence frequency and severity.
///
/// Scoring formula:
///   Start at 100.
///   Each Warning insight occurrence:       −5
///   Each Recommendation insight occurrence: −2
///   Each unresolved insight per day:       −1 additional
///   Clamp to [0, 100].
///
/// A score of 90–100 means minimal issues; below 60 means frequent problems.
/// </summary>
public sealed record HealthScore(
    /// <summary>The score value in [0, 100].</summary>
    int            Score,

    /// <summary>The time window this score covers.</summary>
    TimeSpan       Window,

    /// <summary>Number of Warning-class insight events in the window.</summary>
    int            WarningCount,

    /// <summary>Number of Recommendation-class insight events in the window.</summary>
    int            RecommendationCount,

    /// <summary>Human-readable label: "Excellent", "Good", "Fair", "Poor", "Critical"</summary>
    string         Label,

    /// <summary>Trend vs. the previous equivalent window.</summary>
    TrendDirection Trend);

// ── Recurring insight pattern ──────────────────────────────────────────────────

/// <summary>
/// A system insight that has appeared multiple times in the historical record.
/// Detected by <see cref="HistoricalPatternDetector"/>.
/// </summary>
public sealed record RecurringInsightPattern(
    string         InsightId,
    string         Title,

    /// <summary>Total onset events in the analysis window.</summary>
    int            TotalOccurrences,

    /// <summary>Average duration of each occurrence in minutes (0 when unresolved).</summary>
    double         AvgDurationMinutes,

    /// <summary>Most recent onset time.</summary>
    DateTimeOffset LastSeen,

    /// <summary>
    /// Approximate occurrence frequency label:
    ///   "Daily", "Several times per week", "Weekly", "Occasional"
    /// </summary>
    string         FrequencyLabel,

    /// <summary>True when the issue appears to be worsening in frequency.</summary>
    bool           IsEscalating);

// ── Full historical summary ────────────────────────────────────────────────────

/// <summary>
/// Complete output of <see cref="IHistoricalAnalyticsEngine.ComputeAsync"/>.
/// This record drives the Historical Intelligence page.
/// All fields are computed deterministically from SQLite data — no estimates,
/// no probability models.
/// </summary>
public sealed record HistoricalSummary(
    // ── Coverage ──────────────────────────────────────────────────────────────
    /// <summary>Earliest telemetry sample in the database, or null for new installs.</summary>
    DateTimeOffset?   OldestDataPoint,

    /// <summary>Total telemetry rows across all resolution tiers.</summary>
    long              TotalTelemetryRows,

    /// <summary>Total timeline events persisted.</summary>
    long              TotalTimelineEvents,

    /// <summary>Total insight history rows (onset/resolution pairs).</summary>
    long              TotalInsightEvents,

    // ── Health scores ─────────────────────────────────────────────────────────
    /// <summary>Health score for the past 7 days.</summary>
    HealthScore       SevenDayHealth,

    /// <summary>Health score for the past 30 days.</summary>
    HealthScore       ThirtyDayHealth,

    // ── Metric trends ─────────────────────────────────────────────────────────
    LongTermMetricTrend CpuTrend,
    LongTermMetricTrend RamTrend,
    LongTermMetricTrend DiskTrend,

    // ── Patterns ──────────────────────────────────────────────────────────────
    /// <summary>Recurring insights ordered by frequency descending.</summary>
    IReadOnlyList<RecurringInsightPattern> RecurringPatterns,

    // ── Summary prose ─────────────────────────────────────────────────────────
    /// <summary>One or two paragraph narrative summarising historical trends.</summary>
    string            NarrativeSummary,

    // ── Metadata ──────────────────────────────────────────────────────────────
    DateTimeOffset    ComputedAt);
