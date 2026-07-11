namespace Lucid.Services.Analytics;

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
/// One distinct condition's contribution to a health score — the unit the
/// score-breakdown UI renders so the user can see exactly where points went.
/// </summary>
public sealed record HealthScoreContribution(
    /// <summary>Stable identifier of the insight rule/condition.</summary>
    string         InsightId,

    /// <summary>Human-readable condition title (latest occurrence's title).</summary>
    string         Title,

    /// <summary>Highest severity ordinal seen for this condition in the window.</summary>
    int            SeverityOrdinal,

    /// <summary>How many times the condition fired in the window.</summary>
    int            Occurrences,

    /// <summary>Points deducted from the score for this condition (positive number).</summary>
    int            PointsDeducted,

    /// <summary>First onset inside the window.</summary>
    DateTimeOffset FirstSeen,

    /// <summary>Most recent onset inside the window.</summary>
    DateTimeOffset LastSeen,

    /// <summary>
    /// True when the condition's latest occurrence is still unresolved — i.e.
    /// it is currently active and has a live fix path. Resolved (historical)
    /// conditions are shown for context but no longer offer a "fix".
    /// </summary>
    bool           IsActive);

/// <summary>
/// Operational health score for a rolling time window.
/// Derived from DISTINCT insight conditions, not raw occurrence counts — a
/// single persistent condition that re-fires all week is one bounded finding,
/// not an unbounded pile of penalties.
///
/// Scoring formula (see <see cref="HealthScoreCalculator"/>):
///   Start at 100.
///   Each distinct ACTIVE Warning condition:          −5
///     …and −5 more if it recurred (≥3 onsets)
///   Each distinct ACTIVE Recommendation condition:   −2
///   Each distinct RESOLVED condition:                −1
///     (−3 for a resolved recurring warning)
///   All resolved deductions together capped at:      −15
///   Clamp to [0, 100].
///
/// A score of 90–100 means minimal issues; below 60 means frequent problems.
/// </summary>
public sealed record HealthScore(
    /// <summary>The score value in [0, 100].</summary>
    int            Score,

    /// <summary>The time window this score covers.</summary>
    TimeSpan       Window,

    /// <summary>Number of Warning-class insight events in the window (raw occurrences).</summary>
    int            WarningCount,

    /// <summary>Number of Recommendation-class insight events in the window (raw occurrences).</summary>
    int            RecommendationCount,

    /// <summary>Human-readable label: "Excellent", "Good", "Fair", "Poor", "Needs Attention"</summary>
    string         Label,

    /// <summary>Trend vs. the previous equivalent window.</summary>
    TrendDirection Trend)
{
    /// <summary>
    /// Per-condition score contributions, ordered by points deducted descending.
    /// Empty when the window had no insight activity.
    /// </summary>
    public IReadOnlyList<HealthScoreContribution> Contributions { get; init; } = [];
}

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
