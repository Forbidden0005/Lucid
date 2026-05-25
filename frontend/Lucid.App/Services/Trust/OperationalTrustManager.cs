using Lucid.Services.Timeline;

namespace Lucid.Services.Trust;

/// <summary>
/// Tracks and adapts the operational trust posture over the current session.
///
/// Trust posture is a dynamic signal that reflects the user's recent interaction
/// history with the consent system. It is NOT a permanent setting — it resets
/// to <see cref="TrustPosture.Standard"/> at the start of each session.
///
/// Posture transitions:
///   Standard  → Cautious  : 2+ high-risk denials in the last 30 minutes
///   Cautious  → Restricted: 3+ high-risk denials in the last 30 minutes, OR
///                           user manually sets ObserveOnly mode
///   Any       → Standard  : user explicitly resets via Settings or Companion
///
/// Effects of each posture:
///   Standard   — normal operation, narration is concise
///   Cautious   — consent cards show extra context; narration is more explanatory
///   Restricted — all automation is paused; only guidance text is produced
///
/// The trust manager does NOT change the <see cref="AutomationConsentService"/> mode —
/// it only influences narration tone and the companion overlay's trust badge.
///
/// Thread-safe: all state access is via a lock.
/// </summary>
public sealed class OperationalTrustManager
{
    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly AutomationAuditService       _audit;
    private readonly AutomationConsentService     _consent;
    private readonly ITimelineAggregationService  _timeline;

    // ── State ─────────────────────────────────────────────────────────────────

    private TrustPosture _posture = TrustPosture.Standard;
    private int          _sessionApprovedCount;
    private int          _sessionDeniedCount;
    private DateTimeOffset _sessionStart = DateTimeOffset.Now;

    private readonly object _stateLock = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    public OperationalTrustManager(
        AutomationAuditService      audit,
        AutomationConsentService    consent,
        ITimelineAggregationService timeline)
    {
        _audit    = audit;
        _consent  = consent;
        _timeline = timeline;

        _consent.ConsentGranted += OnConsentGranted;
        _consent.ConsentDenied  += OnConsentDenied;
        _consent.ModeChanged    += OnConsentModeChanged;
    }

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>Current trust posture. Changes are published via <see cref="PostureChanged"/>.</summary>
    public TrustPosture CurrentPosture
    {
        get { lock (_stateLock) { return _posture; } }
    }

    /// <summary>Number of actions the user approved this session.</summary>
    public int SessionApprovedCount
    {
        get { lock (_stateLock) { return _sessionApprovedCount; } }
    }

    /// <summary>Number of actions the user denied this session.</summary>
    public int SessionDeniedCount
    {
        get { lock (_stateLock) { return _sessionDeniedCount; } }
    }

    /// <summary>
    /// Raised whenever the posture changes. Consumers (companion overlay badge,
    /// narration engine) subscribe to this for adaptive behaviour.
    /// </summary>
    public event EventHandler<TrustPosture>? PostureChanged;

    // ── Manual posture control ────────────────────────────────────────────────

    /// <summary>
    /// Resets the trust posture to Standard.
    /// Called when the user explicitly chooses "Resume automation" in the Settings/Companion.
    /// </summary>
    public void ResetPosture()
    {
        TransitionPosture(TrustPosture.Standard, "User manually reset trust posture.");
    }

    // ── Narration hints ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns a short hint sentence to prepend to the companion narration
    /// when the posture is not Standard.
    /// </summary>
    public string? GetPostureNarrationHint() => _posture switch
    {
        TrustPosture.Cautious    =>
            "I'll be extra careful explaining each step before proceeding.",
        TrustPosture.Restricted  =>
            "Automation is currently paused. I'll describe what I would do " +
            "but won't take any action until you resume automation in Settings.",
        _                        => null,
    };

    /// <summary>
    /// Returns true if the current posture permits automation execution.
    /// When false, the orchestrator should treat every step as ObserveOnly.
    /// </summary>
    public bool PermitsExecution =>
        _posture != TrustPosture.Restricted;

    // ── Trust profile ─────────────────────────────────────────────────────────

    /// <summary>
    /// Produces a short plain-English trust profile for the Settings / Companion panel.
    /// </summary>
    public string GetTrustProfile()
    {
        int approved, denied;
        lock (_stateLock)
        {
            approved = _sessionApprovedCount;
            denied   = _sessionDeniedCount;
        }

        var total = approved + denied;
        if (total == 0)
            return "No automation actions have been requested this session.";

        return _posture switch
        {
            TrustPosture.Standard  =>
                $"This session: {approved} action{(approved != 1 ? "s" : "")} approved, " +
                $"{denied} declined. Automation is operating normally.",
            TrustPosture.Cautious  =>
                $"Cautious mode active. {denied} high-risk action{(denied != 1 ? "s were" : " was")} declined " +
                "recently. Extra context is shown on consent cards.",
            TrustPosture.Restricted =>
                "Automation is paused. You can resume it from Settings → Operational Trust.",
            _ => string.Empty,
        };
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnConsentGranted(object? sender, ConsentResponse response)
    {
        lock (_stateLock)
        {
            _sessionApprovedCount++;
        }
    }

    private void OnConsentDenied(object? sender, ConsentResponse response)
    {
        lock (_stateLock)
        {
            _sessionDeniedCount++;
        }

        EvaluatePostureEscalation();
    }

    private void OnConsentModeChanged(object? sender, TrustConsentMode mode)
    {
        if (mode == TrustConsentMode.ObserveOnly)
            TransitionPosture(TrustPosture.Restricted,
                "User switched to ObserveOnly mode.");
        else if (_posture == TrustPosture.Restricted && mode != TrustConsentMode.ObserveOnly)
            TransitionPosture(TrustPosture.Standard,
                "User changed consent mode away from ObserveOnly — restoring standard posture.");
    }

    // ── Posture escalation logic ──────────────────────────────────────────────

    private void EvaluatePostureEscalation()
    {
        var recentDenials = _audit.CountRecentHighRiskDenials(TimeSpan.FromMinutes(30));

        TrustPosture target;

        if (recentDenials >= 3)
            target = TrustPosture.Restricted;
        else if (recentDenials >= 2)
            target = TrustPosture.Cautious;
        else
            target = TrustPosture.Standard;

        TrustPosture current;
        lock (_stateLock) { current = _posture; }

        // Only escalate, never auto-de-escalate based on this path
        // (de-escalation requires an explicit user reset)
        if (target > current)
            TransitionPosture(target, $"Trust posture escalated: {recentDenials} high-risk denials in the last 30 minutes.");
    }

    private void TransitionPosture(TrustPosture newPosture, string reason)
    {
        TrustPosture old;
        lock (_stateLock)
        {
            old = _posture;
            if (_posture == newPosture) return;
            _posture = newPosture;
        }

        var (severity, title) = newPosture switch
        {
            TrustPosture.Cautious    => (TimelineEventSeverity.Warning, "Trust posture: Cautious"),
            TrustPosture.Restricted  => (TimelineEventSeverity.Warning, "Trust posture: Restricted — automation paused"),
            _                        => (TimelineEventSeverity.Good,    "Trust posture: Standard"),
        };

        // Use ConsentDenied as the closest timeline type for a trust posture change;
        // the title and detail carry the actual meaning.
        _timeline.AddExternalEvent(new TimelineEvent
        {
            Id         = TimelineEvent.NewId(),
            Type       = newPosture == TrustPosture.Standard
                             ? TimelineEventType.ConsentGranted
                             : TimelineEventType.ConsentDenied,
            OccurredAt = DateTimeOffset.Now,
            Title      = title,
            Detail     = reason,
            Severity   = severity,
        });

        PostureChanged?.Invoke(this, newPosture);
    }
}
