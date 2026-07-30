using Lucid.Services.Persistence;

namespace Lucid.Services.Analytics;

/// <summary>
/// Production implementation of <see cref="IHistoricalAnalyticsEngine"/>.
///
/// Queries the three persistence repositories to build:
///   • Long-term health scores (7d / 30d)
///   • Per-metric trends (CPU, RAM, Disk — 7d avg vs. 30d avg)
///   • Recurring insight patterns
///   • A deterministic narrative summary paragraph
///
/// Threading:
///   All repository queries are async/await and run on the thread pool.
///   The engine is stateless between calls — safe to call from any thread.
///
/// Cold-start safety:
///   Returns a valid <see cref="HistoricalSummary"/> with zero counts and
///   null metrics when the database has no data yet.
/// </summary>
public sealed class HistoricalAnalyticsEngine : IHistoricalAnalyticsEngine
{
    // Health-score math lives in HealthScoreCalculator (pure + unit-tested).

    private readonly HistoricalTelemetryRepository _telemetry;
    private readonly InsightHistoryRepository       _insights;
    private readonly TimelineEventRepository        _events;

    public HistoricalSummary? LastSummary { get; private set; }

    public HistoricalAnalyticsEngine(
        HistoricalTelemetryRepository telemetry,
        InsightHistoryRepository       insights,
        TimelineEventRepository        events)
    {
        _telemetry = telemetry;
        _insights  = insights;
        _events    = events;
    }

    // ── IHistoricalAnalyticsEngine ────────────────────────────────────────────

    public async Task<HistoricalSummary> ComputeAsync()
    {
        var now  = DateTimeOffset.UtcNow;
        var ago7  = now.Subtract(TimeSpan.FromDays(7));
        var ago14 = now.Subtract(TimeSpan.FromDays(14));
        var ago30 = now.Subtract(TimeSpan.FromDays(30));
        var ago60 = now.Subtract(TimeSpan.FromDays(60));

        // ── Run all queries concurrently ──────────────────────────────────────
        var oldestTask        = _telemetry.GetOldestSampleTimeAsync();
        var telRowsTask       = _telemetry.GetTotalRowCountAsync();
        var evtCountTask      = _events.GetTotalEventCountAsync();
        var insCountTask      = _insights.GetTotalRowCountAsync();

        var samples7Task      = _telemetry.GetSamplesAsync(ago7,  now);
        var samples30Task     = _telemetry.GetSamplesAsync(ago30, now);
        var insights7Task     = _insights.GetInsightHistoryAsync(ago7,  now);
        var insights14Task    = _insights.GetInsightHistoryAsync(ago14, ago7);
        var insights30Task    = _insights.GetInsightHistoryAsync(ago30, now);
        var insights60Task    = _insights.GetInsightHistoryAsync(ago60, ago30);
        var recurringTask     = _insights.GetRecurringInsightsAsync(days: 30, maxCount: 15);

        await Task.WhenAll(
            oldestTask, telRowsTask, evtCountTask, insCountTask,
            samples7Task, samples30Task,
            insights7Task, insights14Task, insights30Task, insights60Task,
            recurringTask).ConfigureAwait(false);

        var samples7   = samples7Task.Result;
        var samples30  = samples30Task.Result;
        var ins7       = insights7Task.Result;
        var ins14      = insights14Task.Result;
        var ins30      = insights30Task.Result;
        var ins60      = insights60Task.Result;
        var recurring  = recurringTask.Result;

        // ── Health scores ─────────────────────────────────────────────────────
        var health7  = ComputeHealthScore(ins7,  ins14, TimeSpan.FromDays(7));
        var health30 = ComputeHealthScore(ins30, ins60, TimeSpan.FromDays(30));

        // ── Metric trends ─────────────────────────────────────────────────────
        // Split samples30 into two halves for trend direction
        var midpoint = ago30 + TimeSpan.FromDays(15);
        var first15  = samples30.Where(s => s.SampledAt < midpoint).ToList();
        var last15   = samples30.Where(s => s.SampledAt >= midpoint).ToList();

        // 7-day average from samples7; 30-day from samples30
        var cpuTrend  = ComputeMetricTrend("CPU",  samples7, samples30, first15, last15, s => s.CpuPercent);
        var ramTrend  = ComputeMetricTrend("RAM",  samples7, samples30, first15, last15, s => s.RamPercent);
        var diskTrend = ComputeMetricTrend("Disk", samples7, samples30, first15, last15, s => s.DiskPercent);

        // ── Patterns ──────────────────────────────────────────────────────────
        var patterns = HistoricalPatternDetector.Detect(recurring);

        // ── Narrative ─────────────────────────────────────────────────────────
        string narrative = BuildNarrative(health7, health30, cpuTrend, ramTrend, diskTrend, patterns);

        var summary = new HistoricalSummary(
            OldestDataPoint:      oldestTask.Result,
            TotalTelemetryRows:   telRowsTask.Result,
            TotalTimelineEvents:  evtCountTask.Result,
            TotalInsightEvents:   insCountTask.Result,
            SevenDayHealth:       health7,
            ThirtyDayHealth:      health30,
            CpuTrend:             cpuTrend,
            RamTrend:             ramTrend,
            DiskTrend:            diskTrend,
            RecurringPatterns:    patterns,
            NarrativeSummary:     narrative,
            ComputedAt:           DateTimeOffset.Now);

        LastSummary = summary;
        return summary;
    }

    // ── Health score calculation ───────────────────────────────────────────────

    private static HealthScore ComputeHealthScore(
        IReadOnlyList<Persistence.PersistedInsightRecord> current,
        IReadOnlyList<Persistence.PersistedInsightRecord> previous,
        TimeSpan window)
        => HealthScoreCalculator.Compute(current, previous, window);

    // ── Metric trend calculation ───────────────────────────────────────────────

    private static LongTermMetricTrend ComputeMetricTrend(
        string                                        name,
        IReadOnlyList<Persistence.PersistedTelemetrySample> samples7,
        IReadOnlyList<Persistence.PersistedTelemetrySample> samples30,
        List<Persistence.PersistedTelemetrySample>          firstHalf,
        List<Persistence.PersistedTelemetrySample>          secondHalf,
        Func<Persistence.PersistedTelemetrySample, double>  selector)
    {
        double? avg7  = samples7.Count  >= 5 ? samples7.Average(selector)  : null;
        double? avg30 = samples30.Count >= 5 ? samples30.Average(selector) : null;

        TrendDirection dir   = TrendDirection.Stable;
        double         chg   = 0;
        string         label = "Insufficient data";

        if (avg7.HasValue && avg30.HasValue && avg30.Value > 0)
        {
            chg = ((avg7.Value - avg30.Value) / avg30.Value) * 100.0;

            dir = chg switch
            {
                > 5  => TrendDirection.Worsening,
                < -5 => TrendDirection.Improving,
                _    => TrendDirection.Stable,
            };

            label = dir switch
            {
                TrendDirection.Worsening => $"▲ {Math.Abs(chg):F0}% vs. 30-day avg",
                TrendDirection.Improving => $"▼ {Math.Abs(chg):F0}% vs. 30-day avg",
                _                        => "Stable",
            };
        }
        else if (avg7.HasValue)
        {
            label = $"~{avg7.Value:F0}% (limited history)";
        }

        return new LongTermMetricTrend(name, avg7, avg30, dir, chg, label);
    }

    // ── Narrative builder ─────────────────────────────────────────────────────

    private static string BuildNarrative(
        HealthScore                       health7,
        HealthScore                       health30,
        LongTermMetricTrend               cpu,
        LongTermMetricTrend               ram,
        LongTermMetricTrend               disk,
        IReadOnlyList<RecurringInsightPattern> patterns)
    {
        var parts = new List<string>();

        // Overall health sentence
        parts.Add(health7.Trend switch
        {
            TrendDirection.Improving => $"System health has improved over the past 7 days ({health7.Label}, score {health7.Score}/100).",
            TrendDirection.Worsening => $"System health has declined over the past 7 days ({health7.Label}, score {health7.Score}/100).",
            _                        => $"System health has been stable over the past 7 days ({health7.Label}, score {health7.Score}/100).",
        });

        // Metric highlights — only mention worsening or improving metrics
        var metricNotes = new List<string>();
        if (cpu.Direction  == TrendDirection.Worsening) metricNotes.Add($"CPU pressure is up ~{cpu.ChangePercent:F0}%");
        if (ram.Direction  == TrendDirection.Worsening) metricNotes.Add($"RAM usage is up ~{ram.ChangePercent:F0}%");
        if (disk.Direction == TrendDirection.Worsening) metricNotes.Add($"Disk I/O is up ~{disk.ChangePercent:F0}%");
        if (cpu.Direction  == TrendDirection.Improving) metricNotes.Add($"CPU pressure is down ~{Math.Abs(cpu.ChangePercent):F0}%");
        if (ram.Direction  == TrendDirection.Improving) metricNotes.Add($"RAM usage is down ~{Math.Abs(ram.ChangePercent):F0}%");

        if (metricNotes.Count > 0)
            parts.Add("Compared to 30-day averages: " + string.Join(", ", metricNotes) + ".");

        // Recurring patterns
        if (patterns.Count > 0)
        {
            var top = patterns[0];
            parts.Add($"\"{top.Title}\" has been the most frequent issue — occurring {top.TotalOccurrences} {(top.TotalOccurrences == 1 ? "time" : "times")} in the past 30 days ({top.FrequencyLabel}).");
        }
        else if (health30.WarningCount == 0)
        {
            parts.Add("No recurring issues have been detected in the past 30 days.");
        }

        return string.Join(" ", parts);
    }
}
