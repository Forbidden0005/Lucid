using Lucid.Services.Baseline;
using Lucid.Services.History;
using Lucid.Services.Persistence;
using Lucid.Services.Telemetry;
using Lucid.Services.Timeline;

namespace Lucid.Services.Replay;

/// <summary>
/// Core implementation of <see cref="IOperationalReplayService"/>.
///
/// Orchestrates session creation, state reconstruction, causal chain analysis,
/// delta computation, and narrative composition for the Operational Replay workspace.
///
/// Threading:
///   All methods are safe to call from the UI thread.
///   CreateSessionAsync() is async due to the history file read.
///   Reconstruction and analysis methods are synchronous but fast (ms range).
///
/// Snapshot caching:
///   Snapshots are not cached — each call to ReconstructStateAt() produces a
///   fresh snapshot. At ~500 events and ~1200 telemetry samples, this is
///   O(n) and takes < 5 ms. Caching can be added when SQLite extends the
///   data window to hours or days.
/// </summary>
internal sealed class OperationalReplayService : IOperationalReplayService
{
    private readonly ITimelineAggregationService    _timeline;
    private readonly ITelemetryHistoryBuffer        _history;
    private readonly IOperationHistoryService       _operationHistory;
    private readonly ISystemBaselineService         _baseline;
    private readonly HistoricalTelemetryRepository? _telHistoryRepo;
    private readonly TimelineEventRepository?       _timelineEventRepo;

    // Window around an action for before/after comparison
    private static readonly TimeSpan ComparisonWindow = TimeSpan.FromMinutes(5);

    // Nearest-action search window
    private static readonly TimeSpan ActionSearchWindow = TimeSpan.FromMinutes(5);

    // Windows longer than this use SQLite; shorter ones use the in-memory buffer.
    private static readonly TimeSpan MemoryBufferDepth = TimeSpan.FromMinutes(28);

    public OperationalReplayService(
        ITimelineAggregationService    timeline,
        ITelemetryHistoryBuffer        history,
        IOperationHistoryService       operationHistory,
        ISystemBaselineService         baseline,
        HistoricalTelemetryRepository? telHistoryRepo    = null,
        TimelineEventRepository?       timelineEventRepo = null)
    {
        _timeline          = timeline;
        _history           = history;
        _operationHistory  = operationHistory;
        _baseline          = baseline;
        _telHistoryRepo    = telHistoryRepo;
        _timelineEventRepo = timelineEventRepo;
    }

    // ── Session creation ──────────────────────────────────────────────────────

    public async Task<ReplaySession> CreateSessionAsync(
        ReplayWindow window = ReplayWindow.FullSession)
    {
        var now = DateTimeOffset.UtcNow;

        // Determine whether this window fits in the in-memory rolling buffer
        // or requires a SQLite query.
        bool isFullSession = window == ReplayWindow.FullSession;
        var  windowSpan    = isFullSession
            ? TimeSpan.MaxValue
            : TimeSpan.FromSeconds((int)window);

        bool useSqlite = _telHistoryRepo is not null
                         && (isFullSession || windowSpan > MemoryBufferDepth);

        List<HistoricalTelemetryPoint> telemetrySeries;
        List<TimelineEvent>            events;

        if (useSqlite)
        {
            // ── SQLite path: supports 1h / 6h / 24h / full-session windows ────
            DateTimeOffset from;
            if (isFullSession)
            {
                // Use the oldest available sample time as the session start.
                var oldest = await _telHistoryRepo!.GetOldestSampleTimeAsync();
                from = oldest ?? now - MemoryBufferDepth;
            }
            else
            {
                from = now - windowSpan;
            }

            // Run both queries concurrently.
            var samplesTask = _telHistoryRepo!.GetSamplesAsync(from, now);
            var eventsTask  = _timelineEventRepo is not null
                ? _timelineEventRepo.GetEventsAsync(from, now, maxCount: 2000)
                : Task.FromResult<IReadOnlyList<PersistedTimelineEvent>>([]);

            await Task.WhenAll(samplesTask, eventsTask).ConfigureAwait(false);

            // SQLite telemetry already sorted ASC by GetSamplesAsync.
            telemetrySeries = samplesTask.Result
                .Select(static s => new HistoricalTelemetryPoint(
                    s.SampledAt,
                    s.CpuPercent,
                    s.RamPercent,
                    s.GpuPercent  ?? 0,
                    s.DiskPercent,
                    s.TempCelsius ?? 0))
                .ToList();

            // SQLite events are DESC — re-order oldest-first for replay.
            events = eventsTask.Result
                .Select(ToTimelineEvent)
                .OrderBy(static e => e.OccurredAt)
                .ToList();
        }
        else
        {
            // ── In-memory path: fast, covers up to ~30 minutes ────────────────
            telemetrySeries = BuildTelemetrySeriesFromMemory();

            events = _timeline.Events
                .OrderBy(static e => e.OccurredAt)
                .ToList();

            if (!isFullSession)
            {
                var cutoff = now - windowSpan;
                telemetrySeries = telemetrySeries
                    .Where(p => p.Timestamp >= cutoff)
                    .ToList();
                events = events
                    .Where(e => e.OccurredAt >= cutoff)
                    .ToList();
            }
        }

        // ── Operation history ─────────────────────────────────────────────────
        var rawActions = await _operationHistory.GetRecentAsync(200).ConfigureAwait(false);
        var actions    = rawActions.OrderBy(static a => a.ExecutedAt).ToList();

        if (!isFullSession)
        {
            var cutoff = now - windowSpan;
            actions = actions.Where(a => a.ExecutedAt >= cutoff).ToList();
        }

        // ── Determine session time range ──────────────────────────────────────
        var allTimestamps = new List<DateTimeOffset>();
        if (telemetrySeries.Count > 0)
        {
            allTimestamps.Add(telemetrySeries[0].Timestamp);
            allTimestamps.Add(telemetrySeries[^1].Timestamp);
        }
        if (events.Count > 0)
        {
            allTimestamps.Add(events[0].OccurredAt);
            allTimestamps.Add(events[^1].OccurredAt);
        }

        var oldest2 = allTimestamps.Count > 0 ? allTimestamps.Min() : now;
        int total   = telemetrySeries.Count + events.Count + actions.Count;

        return new ReplaySession(
            Id:              Guid.NewGuid().ToString("N"),
            CreatedAt:       DateTimeOffset.Now,
            OldestDataPoint: oldest2,
            NewestDataPoint: DateTimeOffset.Now,
            TelemetrySeries: telemetrySeries,
            Events:          events,
            Actions:         actions,
            Baseline:        _baseline.CurrentBaseline,
            TotalDataPoints: total);
    }

    // ── State reconstruction ──────────────────────────────────────────────────

    public SystemStateSnapshot? ReconstructStateAt(
        ReplaySession  session,
        DateTimeOffset targetTime)
    {
        if (targetTime < session.OldestDataPoint || targetTime > session.NewestDataPoint)
            return null;

        // ── Find nearest telemetry point ──────────────────────────────────────
        var telemetry = FindNearestTelemetry(session.TelemetrySeries, targetTime);
        if (telemetry is null && session.TelemetrySeries.Count > 0)
            telemetry = session.TelemetrySeries[0];

        // Fallback: zero point when no telemetry is available
        telemetry ??= new HistoricalTelemetryPoint(targetTime, 0, 0, 0, 0, 0);

        // ── Reconstruct active insights ───────────────────────────────────────
        var activeInsights = ReconstructActiveInsights(session.Events, targetTime);

        // ── Events up to target time ──────────────────────────────────────────
        var eventsToNow = session.Events
            .Where(e => e.OccurredAt <= targetTime)
            .ToList();

        // ── Actions up to target time ─────────────────────────────────────────
        var actionsToNow = session.Actions
            .Where(a => a.ExecutedAt <= targetTime && !a.IsDryRun)
            .ToList();

        // ── Health state label ────────────────────────────────────────────────
        var (label, summary) = ClassifyHealthState(activeInsights);

        // ── Telemetry data points available ───────────────────────────────────
        int dataPoints = session.TelemetrySeries.Count(p => p.Timestamp <= targetTime);

        return new SystemStateSnapshot(
            Timestamp:          targetTime,
            Telemetry:          telemetry,
            ActiveInsights:     activeInsights,
            EventsToNow:        eventsToNow,
            ActionsToNow:       actionsToNow,
            HealthStateLabel:   label,
            NarrativeSummary:   summary,
            DataPointsAvailable: dataPoints);
    }

    // ── Causal chain ──────────────────────────────────────────────────────────

    public CausalChain BuildCausalChain(
        ReplaySession  session,
        DateTimeOffset fromTime,
        DateTimeOffset toTime)
        => CausalChainAnalyzer.Build(session.Events, session.TelemetrySeries, fromTime, toTime);

    // ── Delta analysis ────────────────────────────────────────────────────────

    public OperationalDelta ComputeDelta(SystemStateSnapshot from, SystemStateSnapshot to)
        => OperationalDeltaAnalyzer.Compute(from, to);

    // ── Remediation comparison ────────────────────────────────────────────────

    public RemediationComparison? BuildRemediationComparison(
        ReplaySession   session,
        OperationRecord action)
    {
        var actionTime = action.ExecutedAt;

        // Reconstruct states before and after the action
        var beforeTime = actionTime - ComparisonWindow;
        var afterTime  = actionTime + ComparisonWindow;

        // Clamp to session window
        beforeTime = beforeTime < session.OldestDataPoint ? session.OldestDataPoint : beforeTime;
        afterTime  = afterTime  > session.NewestDataPoint ? session.NewestDataPoint : afterTime;

        var snapshotBefore = ReconstructStateAt(session, beforeTime);
        var snapshotAfter  = ReconstructStateAt(session, afterTime);

        if (snapshotBefore is null || snapshotAfter is null)
            return null;

        var delta = ComputeDelta(snapshotBefore, snapshotAfter);

        bool showsImprovement =
            delta.Direction == DeltaDirection.Improving ||
            delta.ResolvedInsights.Count > delta.NewInsights.Count;

        var narrative = ReplayNarrativeComposer.ComposeRemediationNarrative(
            new RemediationComparison(
                action, snapshotBefore, snapshotAfter, delta, "", showsImprovement));

        string comparisonNarrative = string.Join(" ", narrative.Paragraphs);

        return new RemediationComparison(
            Action:               action,
            SnapshotBefore:       snapshotBefore,
            SnapshotAfter:        snapshotAfter,
            Delta:                delta,
            ComparisonNarrative:  comparisonNarrative,
            ShowsImprovement:     showsImprovement);
    }

    public OperationRecord? FindNearestAction(ReplaySession session, DateTimeOffset cursorTime)
    {
        return session.Actions
            .Where(a => a.IsSuccess && !a.IsDryRun)
            .Where(a => Math.Abs((a.ExecutedAt - cursorTime).TotalSeconds) <=
                        ActionSearchWindow.TotalSeconds)
            .MinBy(a => Math.Abs((a.ExecutedAt - cursorTime).TotalSeconds));
    }

    // ── Narrative composition ─────────────────────────────────────────────────

    public ReplayNarrative ComposeNarrative(ReplaySession session, DateTimeOffset upToTime)
        => ReplayNarrativeComposer.ComposeSessionNarrative(session, upToTime);

    // ── Convenience queries ───────────────────────────────────────────────────

    public IReadOnlyList<TimelineEvent> GetEventsInRange(
        ReplaySession  session,
        DateTimeOffset from,
        DateTimeOffset to)
        => session.Events
            .Where(e => e.OccurredAt >= from && e.OccurredAt <= to)
            .ToList();

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Reconstructs a <see cref="TimelineEvent"/> from a persisted SQLite row.
    /// </summary>
    private static TimelineEvent ToTimelineEvent(PersistedTimelineEvent p) => new()
    {
        Id                  = p.EventId,
        Type                = (TimelineEventType)p.EventTypeOrdinal,
        OccurredAt          = p.OccurredAt,
        Title               = p.Title,
        Detail              = p.Detail,
        Severity            = (TimelineEventSeverity)p.SeverityOrdinal,
        RelatedInsightId    = p.RelatedInsightId,
        AttributedProcesses = p.AttributedProcesses?
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .ToList() ?? [],
        ActionId            = p.ActionId,
    };

    /// <summary>
    /// Queries all five metric arrays from the in-memory rolling buffer and
    /// merges them by index into a single time series. Zipping by index is safe
    /// because all five metrics are written atomically in the same buffer lock.
    /// </summary>
    private List<HistoricalTelemetryPoint> BuildTelemetrySeriesFromMemory()
    {
        var cpu  = _history.GetSamples(TelemetryMetric.Cpu,         TimeWindow.LastThirtyMinutes);
        var ram  = _history.GetSamples(TelemetryMetric.Ram,         TimeWindow.LastThirtyMinutes);
        var gpu  = _history.GetSamples(TelemetryMetric.Gpu,         TimeWindow.LastThirtyMinutes);
        var disk = _history.GetSamples(TelemetryMetric.Disk,        TimeWindow.LastThirtyMinutes);
        var temp = _history.GetSamples(TelemetryMetric.Temperature, TimeWindow.LastThirtyMinutes);

        // Use the minimum count across all metrics to guarantee aligned access
        int count = Math.Min(
            Math.Min(Math.Min(cpu.Count, ram.Count), Math.Min(gpu.Count, disk.Count)),
            temp.Count);

        var series = new List<HistoricalTelemetryPoint>(count);
        for (int i = 0; i < count; i++)
        {
            series.Add(new HistoricalTelemetryPoint(
                Timestamp:            cpu[i].Timestamp,
                CpuPercent:           cpu[i].Value,
                RamPercent:           ram[i].Value,
                GpuPercent:           gpu[i].Value,
                DiskPercent:          disk[i].Value,
                TemperatureCelsius:   temp[i].Value));
        }

        return series;
    }

    /// <summary>
    /// Binary searches the telemetry series (sorted oldest-first) for the
    /// sample with the timestamp closest to <paramref name="targetTime"/>.
    /// </summary>
    private static HistoricalTelemetryPoint? FindNearestTelemetry(
        IReadOnlyList<HistoricalTelemetryPoint> series,
        DateTimeOffset                          targetTime)
    {
        if (series.Count == 0) return null;
        if (series.Count == 1) return series[0];

        // Binary search for insertion point
        int lo = 0, hi = series.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (series[mid].Timestamp < targetTime) lo = mid + 1;
            else hi = mid;
        }

        // lo is now the first index with Timestamp >= targetTime
        if (lo == 0) return series[0];
        if (lo >= series.Count) return series[^1];

        // Return whichever of lo-1 or lo is closer
        var before = series[lo - 1];
        var after  = series[lo];
        var diffBefore = Math.Abs((before.Timestamp - targetTime).Ticks);
        var diffAfter  = Math.Abs((after.Timestamp  - targetTime).Ticks);
        return diffBefore <= diffAfter ? before : after;
    }

    /// <summary>
    /// Reconstructs which insights were active at <paramref name="targetTime"/>
    /// by replaying InsightOnset and InsightResolved events chronologically.
    /// </summary>
    private static IReadOnlyList<ActiveInsightInfo> ReconstructActiveInsights(
        IReadOnlyList<TimelineEvent> events,
        DateTimeOffset               targetTime)
    {
        // Track active insights by their RelatedInsightId
        var active = new Dictionary<string, ActiveInsightInfo>(StringComparer.Ordinal);

        foreach (var ev in events)
        {
            if (ev.OccurredAt > targetTime) break;  // events are oldest-first

            if (ev.Type == TimelineEventType.InsightOnset && ev.RelatedInsightId is not null)
            {
                active[ev.RelatedInsightId] = new ActiveInsightInfo(
                    InsightId:           ev.RelatedInsightId,
                    Title:               ev.Title,
                    OnsetAt:             ev.OccurredAt,
                    Severity:            ev.Severity,
                    AttributedProcesses: ev.AttributedProcesses ?? []);
            }
            else if (ev.Type == TimelineEventType.InsightResolved && ev.RelatedInsightId is not null)
            {
                active.Remove(ev.RelatedInsightId);
            }
            // system.healthy clears everything
            else if (ev.Type == TimelineEventType.InsightOnset
                     && ev.RelatedInsightId == "system.healthy")
            {
                active.Clear();
            }
        }

        return active.Values
            .OrderByDescending(i => (int)i.Severity)
            .ThenBy(i => i.OnsetAt)
            .ToList();
    }

    /// <summary>
    /// Derives a health state label and short summary from the reconstructed active insights.
    /// Mirrors SystemStateClassifier logic but operates on reconstructed data.
    /// </summary>
    private static (string label, string summary) ClassifyHealthState(
        IReadOnlyList<ActiveInsightInfo> insights)
    {
        // Filter out informational system.healthy pseudo-insights
        var significant = insights
            .Where(i => i.InsightId != "system.healthy")
            .ToList();

        int warnings = significant.Count(i => i.Severity == TimelineEventSeverity.Warning);
        int recs     = significant.Count(i => i.Severity == TimelineEventSeverity.Info);

        if (warnings == 0 && recs == 0)
            return ("Healthy", "All monitored metrics were within normal ranges at this moment.");

        if (warnings == 0)
            return ("Monitoring", $"{recs} item(s) noted for review at this moment.");

        if (warnings <= 2)
            return ("Needs attention",
                $"{warnings} warning condition(s) were active — {string.Join(", ", significant.Take(2).Select(i => i.Title))}.");

        return ("Multiple issues",
            $"{warnings} conditions were active at this moment.");
    }
}
