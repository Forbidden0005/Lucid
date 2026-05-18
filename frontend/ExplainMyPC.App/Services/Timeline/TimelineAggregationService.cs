using ExplainMyPC.Services.History;
using ExplainMyPC.Services.Intelligence;
using ExplainMyPC.Services.Narrative;
using ExplainMyPC.Services.Session;
using Microsoft.UI.Dispatching;

namespace ExplainMyPC.Services.Timeline;

/// <summary>
/// Production implementation of <see cref="ITimelineAggregationService"/>.
///
/// Event sources and their conversions:
///
///   Intelligence.InsightsUpdated
///     → Diffs the active insight set against the previous snapshot.
///       New IDs  → InsightOnset / ForecastAlert / SynthesisCorrelated
///       Gone IDs → InsightResolved
///
///   Session.SessionContextChanged
///     → Scans RecentEvents for entries newer than the last-seen high-water mark.
///       Boot, WakeFromSleep, IdleBegin, IdleEnd are emitted as timeline events.
///       InsightOnset/Cleared session-events are skipped (duplicates of above).
///
///   Narrative.NarrativeUpdated
///     → Emits a NarrativeCheckpoint when health state changes, or when more than
///       <see cref="NarrativeCheckpointMinMinutes"/> minutes have elapsed since the
///       last checkpoint. This throttle prevents the timeline from being flooded with
///       narrative ticks that fire on every InsightsUpdated.
///
///   IOperationHistoryService
///     → Loaded once on Start() via an async read; results delivered to _events through
///       DispatcherQueue.TryEnqueue so all mutations remain on the UI thread.
///
/// Threading:
///   Intelligence, session, and narrative events all arrive on the UI thread.
///   _events is therefore mutated exclusively on the UI thread — no locks needed.
///   History records arrive on the thread pool and are marshalled back via the
///   DispatcherQueue injected at construction time.
/// </summary>
public sealed class TimelineAggregationService : ITimelineAggregationService
{
    // ── Configuration ─────────────────────────────────────────────────────────

    private const int    MaxEvents                    = 500;
    private const double NarrativeCheckpointMinMinutes = 5.0;

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly ISystemInsightEngine        _intelligence;
    private readonly ISessionContextService      _session;
    private readonly IOperationalNarrativeEngine _narrative;
    private readonly IOperationHistoryService    _historyService;
    private readonly DispatcherQueue             _dispatcher;

    // ── Event buffer (UI thread only) ─────────────────────────────────────────

    private readonly List<TimelineEvent> _events = new(MaxEvents + 4);

    // ── Insight diff state ────────────────────────────────────────────────────

    /// <summary>Active insight IDs seen on the last InsightsUpdated tick.</summary>
    private readonly HashSet<string> _activeInsightIds = new(StringComparer.Ordinal);

    /// <summary>Maps active insight ID → its display title for richer resolved messages.</summary>
    private readonly Dictionary<string, string> _insightTitles = new(StringComparer.Ordinal);

    // ── Session event diff state ──────────────────────────────────────────────

    /// <summary>
    /// OccurredAt of the newest session event we have already emitted as a timeline event.
    /// Acts as a high-water mark; any RecentEvent newer than this is new.
    /// </summary>
    private DateTimeOffset _lastSessionEventAt = DateTimeOffset.MinValue;

    // ── Narrative checkpoint throttle state ───────────────────────────────────

    private DateTimeOffset?    _lastNarrativeCheckpointAt;
    private SystemHealthState? _lastNarrativeHealthState;

    // ── Public surface ────────────────────────────────────────────────────────

    public IReadOnlyList<TimelineEvent> Events => _events.AsReadOnly();
    public event EventHandler<TimelineEvent>? NewEventAdded;

    // ── Construction ──────────────────────────────────────────────────────────

    public TimelineAggregationService(
        ISystemInsightEngine        intelligence,
        ISessionContextService      session,
        IOperationalNarrativeEngine narrative,
        IOperationHistoryService    historyService,
        DispatcherQueue             uiDispatcher)
    {
        _intelligence   = intelligence;
        _session        = session;
        _narrative      = narrative;
        _historyService = historyService;
        _dispatcher     = uiDispatcher;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Start()
    {
        // Seed from session events already tracked (boot, prior wakes, etc.).
        SeedFromSession(_session.Current);

        // Seed from currently active intelligence findings.
        if (_intelligence.CurrentInsights.Count > 0)
            SeedFromInsights(_intelligence.CurrentInsights);

        // Load persisted operation history on the thread pool; marshal back to UI.
        _ = LoadHistoryAsync();

        // Subscribe after seeding to avoid double-emitting initial state.
        _intelligence.InsightsUpdated  += OnInsightsUpdated;
        _session.SessionContextChanged += OnSessionContextChanged;
        _narrative.NarrativeUpdated    += OnNarrativeUpdated;
    }

    public void Stop()
    {
        _intelligence.InsightsUpdated  -= OnInsightsUpdated;
        _session.SessionContextChanged -= OnSessionContextChanged;
        _narrative.NarrativeUpdated    -= OnNarrativeUpdated;
    }

    // ── Seeding helpers (called on UI thread during Start) ────────────────────

    private void SeedFromSession(SessionContext ctx)
    {
        // RecentEvents is newest-first. Reverse to process oldest-first so that
        // AddEvent (which inserts at index 0) leaves _events in newest-first order.
        foreach (var ev in ctx.RecentEvents.Reverse())
        {
            // InsightOnset/Cleared are covered with richer data via OnInsightsUpdated.
            if (ev.Kind is SessionEventKind.InsightOnset or SessionEventKind.InsightCleared)
                continue;

            AddEvent(SessionEventToTimeline(ev), notify: false); // silent during seeding
        }

        // Set high-water mark to the newest session event we've seen.
        if (ctx.RecentEvents.Count > 0)
            _lastSessionEventAt = ctx.RecentEvents[0].OccurredAt;
    }

    private void SeedFromInsights(IReadOnlyList<SystemInsight> insights)
    {
        var ctx = _session.Current;
        foreach (var insight in insights)
        {
            if (!_activeInsightIds.Add(insight.Id)) continue;
            _insightTitles[insight.Id] = insight.Title;

            // Use session onset time when available for accurate timestamps.
            var onsetAt = ctx.InsightOnsets.TryGetValue(insight.Id, out var t)
                ? t : insight.DetectedAt;

            AddEvent(InsightToOnsetEvent(insight, onsetAt), notify: false);
        }
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            // Read on thread pool; OperationHistoryService uses SemaphoreSlim internally.
            var records = await _historyService.GetRecentAsync(50).ConfigureAwait(false);

            // Marshal back to UI thread before mutating _events.
            _dispatcher.TryEnqueue(() =>
            {
                // Records arrive newest-first; insert oldest-first so AddEvent leaves
                // _events in newest-first order.
                foreach (var record in records.Reverse())
                    AddEvent(HistoryRecordToTimeline(record), notify: true);
            });
        }
        catch
        {
            // History load is best-effort — never disrupt the timeline service.
        }
    }

    // ── Live event handlers (all on UI thread) ────────────────────────────────

    private void OnInsightsUpdated(object? sender, IReadOnlyList<SystemInsight> insights)
    {
        var now      = DateTimeOffset.Now;
        var activeIds = new HashSet<string>(insights.Select(i => i.Id), StringComparer.Ordinal);

        // Detect cleared insights → InsightResolved.
        foreach (var id in _activeInsightIds.Where(id => !activeIds.Contains(id)).ToList())
        {
            var title = _insightTitles.TryGetValue(id, out var t) ? t : id;
            AddEvent(new TimelineEvent
            {
                Id               = TimelineEvent.NewId(),
                Type             = TimelineEventType.InsightResolved,
                OccurredAt       = now,
                Title            = $"{title} resolved",
                Detail           = "This condition cleared — the system returned to normal on this metric.",
                Severity         = TimelineEventSeverity.Good,
                RelatedInsightId = id,
            });
            _activeInsightIds.Remove(id);
            _insightTitles.Remove(id);
        }

        // Detect new insights → onset, forecast, or synthesis events.
        var ctx = _session.Current;
        foreach (var insight in insights)
        {
            if (!_activeInsightIds.Add(insight.Id)) continue;
            _insightTitles[insight.Id] = insight.Title;

            var onsetAt = ctx.InsightOnsets.TryGetValue(insight.Id, out var t) ? t : now;
            AddEvent(InsightToOnsetEvent(insight, onsetAt));
        }
    }

    private void OnSessionContextChanged(object? sender, SessionContext ctx)
    {
        // Emit events for any session events newer than our high-water mark.
        // RecentEvents is newest-first; TakeWhile stops at already-seen events.
        foreach (var ev in ctx.RecentEvents.TakeWhile(e => e.OccurredAt > _lastSessionEventAt))
        {
            if (ev.Kind is SessionEventKind.InsightOnset or SessionEventKind.InsightCleared)
                continue;
            AddEvent(SessionEventToTimeline(ev));
        }

        if (ctx.RecentEvents.Count > 0)
            _lastSessionEventAt = ctx.RecentEvents[0].OccurredAt;
    }

    private void OnNarrativeUpdated(object? sender, OperationalNarrative narrative)
    {
        bool healthChanged = _lastNarrativeHealthState.HasValue
                          && _lastNarrativeHealthState != narrative.HealthState;

        bool timedOut = _lastNarrativeCheckpointAt is null
                     || (DateTimeOffset.Now - _lastNarrativeCheckpointAt.Value).TotalMinutes
                        >= NarrativeCheckpointMinMinutes;

        if (!healthChanged && !timedOut) return;

        _lastNarrativeHealthState  = narrative.HealthState;
        _lastNarrativeCheckpointAt = DateTimeOffset.Now;

        AddEvent(new TimelineEvent
        {
            Id         = TimelineEvent.NewId(),
            Type       = TimelineEventType.NarrativeCheckpoint,
            OccurredAt = DateTimeOffset.Now,
            Title      = narrative.Headline,
            Detail     = narrative.StatusParagraph.Length > 0 ? narrative.StatusParagraph : null,
            Severity   = narrative.HealthState switch
            {
                SystemHealthState.Critical   => TimelineEventSeverity.Critical,
                SystemHealthState.Degraded   => TimelineEventSeverity.Warning,
                SystemHealthState.Monitoring => TimelineEventSeverity.Info,
                _                            => TimelineEventSeverity.Good,
            },
        });
    }

    // ── Conversion helpers ────────────────────────────────────────────────────

    private static TimelineEvent InsightToOnsetEvent(SystemInsight insight, DateTimeOffset onsetAt)
    {
        // Classify by rule ID prefix: forecast.* and synthesis.* get distinct types.
        var type = insight.Id.StartsWith("forecast.",  StringComparison.Ordinal) ? TimelineEventType.ForecastAlert
                 : insight.Id.StartsWith("synthesis.", StringComparison.Ordinal) ? TimelineEventType.SynthesisCorrelated
                 : TimelineEventType.InsightOnset;

        var severity = insight.Severity switch
        {
            InsightSeverity.Warning        => TimelineEventSeverity.Warning,
            InsightSeverity.Recommendation => TimelineEventSeverity.Info,
            _                              => TimelineEventSeverity.Info,
        };

        // Collect process display names for attribution chips.
        IReadOnlyList<string>? processes = insight.AttributedProcesses.Count > 0
            ? insight.AttributedProcesses.Select(p => p.DisplayName).Distinct().ToList()
            : null;

        return new TimelineEvent
        {
            Id                  = TimelineEvent.NewId(),
            Type                = type,
            OccurredAt          = onsetAt,
            Title               = insight.Title,
            Detail              = insight.Detail,
            Severity            = severity,
            RelatedInsightId    = insight.Id,
            AttributedProcesses = processes,
        };
    }

    private static TimelineEvent SessionEventToTimeline(SessionEvent ev)
    {
        var (type, title, detail, severity) = ev.Kind switch
        {
            SessionEventKind.SystemBoot =>
                (TimelineEventType.SystemBoot, "System started",
                 "Windows finished booting. ExplainMyPC will monitor from this point.",
                 TimelineEventSeverity.Info),

            SessionEventKind.AppSessionStart =>
                (TimelineEventType.SessionStart, "ExplainMyPC started",
                 "Monitoring session began. Baseline learning and telemetry collection are active.",
                 TimelineEventSeverity.Info),

            SessionEventKind.WakeFromSleep =>
                (TimelineEventType.WakeFromSleep, "Resumed from sleep",
                 "System woke from sleep or hibernation. Post-wake resource activity may be elevated briefly.",
                 TimelineEventSeverity.Info),

            SessionEventKind.IdleBegin =>
                (TimelineEventType.IdleBegin, "System entered idle state",
                 "CPU usage dropped below 15% and remained low for 3+ minutes.",
                 TimelineEventSeverity.Good),

            SessionEventKind.IdleEnd =>
                (TimelineEventType.IdleEnd, "Activity resumed",
                 "CPU usage returned above idle threshold after an idle period.",
                 TimelineEventSeverity.Info),

            _ => (TimelineEventType.SessionStart, "Session event", null, TimelineEventSeverity.Info),
        };

        return new TimelineEvent
        {
            Id         = TimelineEvent.NewId(),
            Type       = type,
            OccurredAt = ev.OccurredAt,
            Title      = title,
            Detail     = detail,
            Severity   = severity,
        };
    }

    private static TimelineEvent HistoryRecordToTimeline(OperationRecord record)
    {
        var type = record.IsRollback ? TimelineEventType.ActionRollback
                 : record.IsDryRun  ? TimelineEventType.ActionPreview
                 : record.IsSuccess ? TimelineEventType.ActionExecuted
                 :                    TimelineEventType.ActionFailed;

        var severity = record.IsRollback ? TimelineEventSeverity.Info
                     : record.IsDryRun   ? TimelineEventSeverity.Info
                     : record.IsSuccess  ? TimelineEventSeverity.Good
                     :                     TimelineEventSeverity.Warning;

        // Compose a concise detail from the outcome message plus any errors.
        var detailParts = new List<string>();
        if (!string.IsNullOrEmpty(record.Message)) detailParts.Add(record.Message);
        detailParts.AddRange(record.Errors.Take(2));
        var detail = detailParts.Count > 0 ? string.Join("\n", detailParts) : null;

        return new TimelineEvent
        {
            Id         = TimelineEvent.NewId(),
            Type       = type,
            OccurredAt = record.ExecutedAt,
            Title      = record.ActionTitle,
            Detail     = detail,
            Severity   = severity,
            ActionId   = record.ActionId,
        };
    }

    // ── Buffer management (UI thread only) ────────────────────────────────────

    private void AddEvent(TimelineEvent ev, bool notify = true)
    {
        _events.Insert(0, ev); // newest-first

        // Trim to cap — remove oldest (tail).
        if (_events.Count > MaxEvents)
            _events.RemoveAt(_events.Count - 1);

        if (notify)
            NewEventAdded?.Invoke(this, ev);
    }
}
