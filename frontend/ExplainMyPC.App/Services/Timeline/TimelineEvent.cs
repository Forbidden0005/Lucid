namespace ExplainMyPC.Services.Timeline;

/// <summary>Severity tier for a timeline event — drives colour coding in the UI.</summary>
public enum TimelineEventSeverity
{
    Info     = 0,
    Good     = 1,
    Warning  = 2,
    Critical = 3,
}

/// <summary>
/// Discriminated type covering every event category the timeline can display.
/// Values are grouped into ranges: 0–9 session, 10–19 intelligence, 20–29 narrative,
/// 30–39 actions, so future additions don't collide.
/// </summary>
public enum TimelineEventType
{
    // ── Session ───────────────────────────────────────────────────────────────
    /// <summary>Windows finished booting.</summary>
    SystemBoot          =  0,
    /// <summary>ExplainMyPC was launched — used as a proxy for user session start.</summary>
    SessionStart        =  1,
    /// <summary>System resumed from sleep or hibernation (gap-heuristic detected).</summary>
    WakeFromSleep       =  2,
    /// <summary>CPU fell below 15 % for ≥ 3 continuous minutes.</summary>
    IdleBegin           =  3,
    /// <summary>CPU returned above idle threshold after an idle period.</summary>
    IdleEnd             =  4,

    // ── Intelligence ──────────────────────────────────────────────────────────
    /// <summary>A new insight rule triggered — a finding appeared in the active set.</summary>
    InsightOnset        = 10,
    /// <summary>An active finding cleared — the condition no longer holds.</summary>
    InsightResolved     = 11,
    /// <summary>A forecast-class rule fired (rule ID prefix: forecast.*).</summary>
    ForecastAlert       = 12,
    /// <summary>The synthesis engine detected a cross-insight process correlation.</summary>
    SynthesisCorrelated = 13,

    // ── Narrative ─────────────────────────────────────────────────────────────
    /// <summary>The narrative engine produced a meaningfully updated prose summary.</summary>
    NarrativeCheckpoint = 20,

    // ── Actions ───────────────────────────────────────────────────────────────
    /// <summary>A remediation action completed successfully.</summary>
    ActionExecuted      = 30,
    /// <summary>A dry-run preview of an action was run.</summary>
    ActionPreview       = 31,
    /// <summary>A previously executed action was rolled back.</summary>
    ActionRollback      = 32,
    /// <summary>An action execution failed.</summary>
    ActionFailed        = 33,
}

/// <summary>
/// Immutable record representing a single point-in-time event in the operational timeline.
///
/// Produced by <see cref="ITimelineAggregationService"/> from four upstream sources:
///   • Intelligence engine  — insight onsets/clears, forecast alerts, synthesis correlations
///   • Session context      — system boot, wake from sleep, idle begin/end
///   • Narrative engine     — checkpoints when health state or prose changes materially
///   • Operation history    — executed actions, previews, rollbacks (loaded on Start)
///
/// Threading: once created, <see cref="TimelineEvent"/> is fully immutable and safe to
/// read from any thread. All instances flow through <see cref="ITimelineAggregationService"/>
/// which guarantees delivery on the UI thread.
/// </summary>
public sealed record TimelineEvent
{
    /// <summary>Unique identifier (32-char lowercase hex, no hyphens).</summary>
    public required string                 Id                  { get; init; }

    /// <summary>Discriminated type — determines glyph, colour group, and filter category.</summary>
    public required TimelineEventType      Type                { get; init; }

    /// <summary>Wall-clock time when the event occurred.</summary>
    public required DateTimeOffset         OccurredAt          { get; init; }

    /// <summary>Short human-readable title shown on the event card.</summary>
    public required string                 Title               { get; init; }

    /// <summary>
    /// Optional longer explanation shown when the card is expanded.
    /// Null for session events with no additional context.
    /// </summary>
    public          string?                Detail              { get; init; }

    /// <summary>Severity tier — drives the left accent stripe colour.</summary>
    public required TimelineEventSeverity  Severity            { get; init; }

    /// <summary>
    /// ID of the <see cref="Services.Intelligence.SystemInsight"/> that originated
    /// this event. Enables future deep-link navigation to the Intelligence page.
    /// Null for session and action events.
    /// </summary>
    public          string?                RelatedInsightId    { get; init; }

    /// <summary>
    /// Display names of processes attributed to this finding by the intelligence engine.
    /// Null or empty when no attribution data is available.
    /// </summary>
    public          IReadOnlyList<string>? AttributedProcesses { get; init; }

    /// <summary>Action ID when the event originated from the execution engine.</summary>
    public          string?                ActionId            { get; init; }

    /// <summary>Factory helper — new random event ID, 32 lowercase hex chars.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");
}
