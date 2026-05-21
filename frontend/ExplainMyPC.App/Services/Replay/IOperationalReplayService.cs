using ExplainMyPC.Services.History;
using ExplainMyPC.Services.Timeline;

namespace ExplainMyPC.Services.Replay;

/// <summary>
/// Operational replay and causal investigation engine.
///
/// Transforms ExplainMyPC from a "current state" monitor into an
/// "operational memory" platform — letting users understand how their
/// system got into its current state, what caused degradation, and
/// what effect their remediations had.
///
/// Data sources used for reconstruction:
///   • TelemetryHistoryBuffer   — 30-minute rolling hardware metrics
///   • TimelineAggregationService — chronological event stream (500 events, session-scoped)
///   • OperationHistoryService  — persistent action history (cross-session)
///   • SystemBaselineService    — machine-specific behavioral norms
///
/// Threading:
///   All public methods are safe to call from the UI thread.
///   CreateSessionAsync() is async due to the history file read.
///   All other methods are synchronous and lightweight.
///
/// Replay depth:
///   Currently limited to the in-memory 30-minute telemetry window.
///   SQLite persistence (future Phase 1) will extend this to 24h+.
/// </summary>
public interface IOperationalReplayService
{
    // ── Session creation ──────────────────────────────────────────────────────

    /// <summary>
    /// Captures a complete snapshot of all available system data and returns it
    /// as an immutable <see cref="ReplaySession"/> ready for scrubbing.
    ///
    /// Telemetry is clamped to the requested <paramref name="window"/>; if the
    /// buffer has less data than the window, all available data is returned.
    ///
    /// The returned session is immutable — call this again to refresh.
    /// </summary>
    Task<ReplaySession> CreateSessionAsync(ReplayWindow window = ReplayWindow.FullSession);

    // ── State reconstruction ──────────────────────────────────────────────────

    /// <summary>
    /// Reconstructs the full system state at the given <paramref name="targetTime"/>
    /// by replaying the event stream and finding the nearest telemetry sample.
    ///
    /// Returns null when <paramref name="targetTime"/> is outside the session window.
    /// </summary>
    SystemStateSnapshot? ReconstructStateAt(ReplaySession session, DateTimeOffset targetTime);

    // ── Causal investigation ──────────────────────────────────────────────────

    /// <summary>
    /// Builds a causal chain describing how conditions escalated within the
    /// given time range, including linked telemetry shifts, insight onsets,
    /// process attributions, and any remediations.
    ///
    /// Returns an empty chain when no significant events exist in the range.
    /// </summary>
    CausalChain BuildCausalChain(
        ReplaySession  session,
        DateTimeOffset fromTime,
        DateTimeOffset toTime);

    // ── Delta analysis ────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the operational delta (what changed) between two state snapshots.
    /// Returns a <see cref="OperationalDelta"/> with per-metric diffs, insight
    /// changes, action changes, and human-readable summary lines.
    /// </summary>
    OperationalDelta ComputeDelta(SystemStateSnapshot from, SystemStateSnapshot to);

    // ── Remediation comparison ────────────────────────────────────────────────

    /// <summary>
    /// Builds a before/after comparison for a specific remediation action by
    /// reconstructing system state 5 minutes before and 5 minutes after the action.
    ///
    /// Returns null when insufficient telemetry data exists around the action time.
    /// </summary>
    RemediationComparison? BuildRemediationComparison(
        ReplaySession   session,
        OperationRecord action);

    /// <summary>
    /// Returns the completed (non-dry-run) action closest to the given cursor time,
    /// within a ±5-minute window, or null if none exists nearby.
    /// </summary>
    OperationRecord? FindNearestAction(ReplaySession session, DateTimeOffset cursorTime);

    // ── Narrative composition ─────────────────────────────────────────────────

    /// <summary>
    /// Composes a plain-English operational narrative describing what happened
    /// from the session start up to <paramref name="upToTime"/>.
    /// </summary>
    ReplayNarrative ComposeNarrative(ReplaySession session, DateTimeOffset upToTime);

    // ── Convenience queries ───────────────────────────────────────────────────

    /// <summary>
    /// Returns all <see cref="TimelineEvent"/>s from the session that fall within
    /// the given time range, sorted oldest-first.
    /// </summary>
    IReadOnlyList<TimelineEvent> GetEventsInRange(
        ReplaySession  session,
        DateTimeOffset from,
        DateTimeOffset to);
}
