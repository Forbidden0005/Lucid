using Lucid.Helpers;
using Lucid.Services.Intelligence;

namespace Lucid.Services.Session;

/// <summary>
/// Production implementation of <see cref="ISessionContextService"/>.
///
/// Subscribes to two upstream feeds:
///   1. <see cref="ITelemetryService.ReadingAvailable"/> — for idle detection
///      and sleep/wake gap-heuristic detection.
///   2. <see cref="ISystemInsightEngine.InsightsUpdated"/> — for onset tracking.
///
/// Sleep/wake detection:
///   No special OS API or NuGet package is required. When the system sleeps,
///   the telemetry polling timer suspends. The first tick after wake arrives
///   with a wall-clock gap far larger than the normal 1.5-second poll interval.
///   Any gap exceeding <see cref="WakeGapThresholdSeconds"/> is classified as a
///   probable resume event. This is reliable for gaps &gt; 45 seconds, which
///   covers all realistic sleep scenarios (suspend, hibernate, display-off
///   with connected-standby disabled).
///
/// Idle detection:
///   "Idle" is defined as CPU % below <see cref="IdleCpuThreshold"/> for at
///   least <see cref="IdleMinTicks"/> consecutive ticks (~3 minutes at 1.5 s/tick).
///   This threshold avoids brief CPU dips during active use being classified as
///   idle periods.
///
/// Onset tracking:
///   A dictionary maps each active insight rule ID to the first time it appeared
///   during this session. Entries are removed when a finding clears. This provides
///   "active for N minutes" and "appeared shortly after wake" context for the
///   narrative engine.
///
/// Threading:
///   ReadingAvailable and InsightsUpdated both fire on the UI thread.
///   All state mutations happen on the UI thread — no locking required.
///   The <see cref="SessionContext"/> snapshot is replaced atomically (reference
///   assignment), so readers always see a consistent immutable snapshot.
/// </summary>
public sealed class SessionContextService : ISessionContextService
{
    // ── Thresholds ────────────────────────────────────────────────────────────

    /// <summary>
    /// A telemetry gap exceeding this many seconds is classified as a wake event.
    /// 45 s comfortably covers the normal poll jitter (≤ 3 s) while catching
    /// even brief screen-off / connected-standby resume cycles.
    /// </summary>
    private const double WakeGapThresholdSeconds = 45.0;

    /// <summary>CPU % below this level counts as "idle" for the session context.</summary>
    private const double IdleCpuThreshold = 15.0;

    /// <summary>
    /// Minimum consecutive low-CPU ticks before the system is classified as idle.
    /// 120 ticks × 1.5 s/tick ≈ 3 minutes.
    /// </summary>
    private const int IdleMinTicks = 120;

    /// <summary>Maximum number of events kept in the recent event timeline.</summary>
    private const int MaxRecentEvents = 50;

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly ITelemetryService    _telemetry;
    private readonly ISystemInsightEngine _insights;

    // ── System-level timing ───────────────────────────────────────────────────

    /// <summary>
    /// Approximate system boot time derived from <see cref="Environment.TickCount64"/>.
    /// Computed once at class initialisation — accurate to within a few seconds.
    /// </summary>
    private static readonly DateTimeOffset s_bootTime =
        DateTimeOffset.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);

    private readonly DateTimeOffset _sessionStart = DateTimeOffset.Now;

    // ── Wake detection state ──────────────────────────────────────────────────

    private DateTimeOffset  _lastTickTime = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastWakeTime;

    // ── Idle detection state ──────────────────────────────────────────────────

    private int              _lowCpuTicks;
    private bool             _isIdle;
    private DateTimeOffset?  _idleStartTime;

    // ── Insight onset tracking ────────────────────────────────────────────────

    /// <summary>
    /// Maps active insight rule IDs to the moment they first appeared.
    /// Cleared when a finding resolves. Modified only on the UI thread.
    /// </summary>
    private readonly Dictionary<string, DateTimeOffset> _onsets = new(StringComparer.Ordinal);

    // ── Event timeline ────────────────────────────────────────────────────────

    private readonly List<SessionEvent> _events = new(MaxRecentEvents + 4);

    // ── Snapshot ──────────────────────────────────────────────────────────────

    private SessionContext _current;

    public SessionContext Current => _current;
    public event EventHandler<SessionContext>? SessionContextChanged;

    // ── Construction ──────────────────────────────────────────────────────────

    public SessionContextService(ITelemetryService telemetry, ISystemInsightEngine insights)
    {
        _telemetry = telemetry;
        _insights  = insights;

        // Record the boot and session-start events.
        AddEvent(new SessionEvent(SessionEventKind.SystemBoot,      s_bootTime));
        AddEvent(new SessionEvent(SessionEventKind.AppSessionStart, _sessionStart));

        _current = BuildSnapshot();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Start()
    {
        _telemetry.ReadingAvailable += OnReadingAvailable;
        _insights.InsightsUpdated   += OnInsightsUpdated;

        // Seed onset tracking from whatever insights are already active.
        if (_insights.CurrentInsights.Count > 0)
        {
            SyncOnsets(_insights.CurrentInsights);
            RebuildAndNotify();
        }
    }

    public void Stop()
    {
        _telemetry.ReadingAvailable -= OnReadingAvailable;
        _insights.InsightsUpdated   -= OnInsightsUpdated;
    }

    // ── Telemetry handler (UI thread) ─────────────────────────────────────────

    private void OnReadingAvailable(object? sender, TelemetrySnapshot snap)
    {
        bool changed = false;
        var  now     = DateTimeOffset.Now;

        // ── Sleep/wake detection ──────────────────────────────────────────────
        // The telemetry service polls every ~1.5 s. Any larger gap indicates
        // the machine was asleep (or the app was suspended by Windows).
        if (_lastTickTime != DateTimeOffset.MinValue)
        {
            double gapSeconds = (now - _lastTickTime).TotalSeconds;

            if (gapSeconds > WakeGapThresholdSeconds)
            {
                // Record the approximate wake time (the first tick after resume
                // is the earliest point we can detect). The actual system resume
                // event occurred somewhere between _lastTickTime and now.
                _lastWakeTime = now;
                AddEvent(new SessionEvent(SessionEventKind.WakeFromSleep, now));
                changed = true;
            }
        }

        _lastTickTime = now;

        // ── Idle detection ────────────────────────────────────────────────────
        if (snap.CpuPercent < IdleCpuThreshold)
        {
            _lowCpuTicks++;

            if (_lowCpuTicks >= IdleMinTicks && !_isIdle)
            {
                _isIdle = true;

                // Back-estimate idle start from accumulated low-CPU ticks.
                _idleStartTime = now - TimeSpan.FromSeconds(_lowCpuTicks * 1.5);
                AddEvent(new SessionEvent(SessionEventKind.IdleBegin, _idleStartTime.Value));
                changed = true;
            }
        }
        else
        {
            if (_isIdle)
            {
                AddEvent(new SessionEvent(SessionEventKind.IdleEnd, now));
                _isIdle        = false;
                _idleStartTime = null;
                changed        = true;
            }

            _lowCpuTicks = 0;
        }

        if (changed) RebuildAndNotify();
    }

    // ── Intelligence handler (UI thread) ──────────────────────────────────────

    private void OnInsightsUpdated(object? sender, IReadOnlyList<SystemInsight> insights)
    {
        SyncOnsets(insights);
        RebuildAndNotify();
    }

    // ── Onset synchronisation ─────────────────────────────────────────────────

    private void SyncOnsets(IReadOnlyList<SystemInsight> insights)
    {
        var now      = DateTimeOffset.Now;
        var activeIds = new HashSet<string>(insights.Select(i => i.Id), StringComparer.Ordinal);

        // Register first appearance of insights not yet in the dictionary.
        foreach (var insight in insights)
        {
            if (!_onsets.ContainsKey(insight.Id))
            {
                _onsets[insight.Id] = now;
                AddEvent(new SessionEvent(SessionEventKind.InsightOnset, now, insight.Id));
            }
        }

        // Remove cleared insights and emit cleared events.
        foreach (var key in _onsets.Keys.Where(k => !activeIds.Contains(k)).ToList())
        {
            AddEvent(new SessionEvent(SessionEventKind.InsightCleared, now, key));
            _onsets.Remove(key);
        }
    }

    // ── Snapshot builder ──────────────────────────────────────────────────────

    private void RebuildAndNotify()
    {
        _current = BuildSnapshot();
        SessionContextChanged?.Invoke(this, _current);
    }

    private SessionContext BuildSnapshot()
    {
        TimeSpan? idleDuration = _isIdle && _idleStartTime.HasValue
            ? DateTimeOffset.Now - _idleStartTime.Value
            : null;

        return new SessionContext
        {
            SystemBootTime = s_bootTime,
            SessionStart   = _sessionStart,
            LastWakeTime   = _lastWakeTime,
            IsIdle         = _isIdle,
            IdleDuration   = idleDuration,
            InsightOnsets  = new Dictionary<string, DateTimeOffset>(_onsets, StringComparer.Ordinal),
            RecentEvents   = _events.AsReadOnly(),
        };
    }

    // ── Event timeline management ─────────────────────────────────────────────

    private void AddEvent(SessionEvent ev)
    {
        _events.Insert(0, ev);  // newest-first

        // Trim to cap — remove from the end (oldest).
        if (_events.Count > MaxRecentEvents)
            _events.RemoveAt(_events.Count - 1);
    }
}
