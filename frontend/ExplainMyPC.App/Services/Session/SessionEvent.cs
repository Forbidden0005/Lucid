namespace ExplainMyPC.Services.Session;

/// <summary>
/// Classifies the type of operational event recorded in the session timeline.
/// </summary>
public enum SessionEventKind
{
    /// <summary>Windows finished booting and the user session started.</summary>
    SystemBoot,

    /// <summary>
    /// ExplainMyPC was launched (used as a proxy for the active user session start).
    /// The first event in every session timeline.
    /// </summary>
    AppSessionStart,

    /// <summary>
    /// The system resumed from sleep or hibernation.
    /// Detected by a gap in telemetry ticks larger than the sleep-detection threshold.
    /// </summary>
    WakeFromSleep,

    /// <summary>
    /// CPU usage dropped below the idle threshold for an extended period.
    /// Represents the system transitioning from active workload to background-only state.
    /// </summary>
    IdleBegin,

    /// <summary>
    /// CPU usage rose above the idle threshold after an idle period.
    /// </summary>
    IdleEnd,

    /// <summary>
    /// An intelligence finding first appeared in the active insight set.
    /// <see cref="SessionEvent.AssociatedId"/> holds the insight's rule ID.
    /// </summary>
    InsightOnset,

    /// <summary>
    /// An intelligence finding cleared from the active insight set.
    /// <see cref="SessionEvent.AssociatedId"/> holds the cleared rule ID.
    /// </summary>
    InsightCleared,
}

/// <summary>
/// A single timestamped operational event in the session timeline.
///
/// Events are recorded by <see cref="ISessionContextService"/> and form the
/// basis for temporal reasoning in the narrative engine:
///   "CPU pressure began 4 minutes after the system woke from sleep."
///   "Memory usage has been elevated for 12 minutes — since startup settled."
/// </summary>
public sealed record SessionEvent(
    /// <summary>The kind of event.</summary>
    SessionEventKind Kind,

    /// <summary>When the event occurred (local time with offset).</summary>
    DateTimeOffset OccurredAt,

    /// <summary>
    /// Optional identifier associated with this event.
    /// For <see cref="SessionEventKind.InsightOnset"/> and
    /// <see cref="SessionEventKind.InsightCleared"/>: the insight rule ID.
    /// Null for system-level events (boot, wake, idle).
    /// </summary>
    string? AssociatedId = null)
{
    /// <summary>How long ago this event occurred.</summary>
    public TimeSpan Age => DateTimeOffset.Now - OccurredAt;
}
