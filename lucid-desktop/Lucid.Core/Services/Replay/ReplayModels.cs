using Lucid.Services.Baseline;
using Lucid.Services.History;
using Lucid.Services.Timeline;

namespace Lucid.Services.Replay;

// ── Time window presets ────────────────────────────────────────────────────────

/// <summary>
/// Named replay window lengths the user can select.
/// Value is the window duration in seconds (same convention as TimeWindow enum).
/// </summary>
public enum ReplayWindow
{
    Last5Minutes   =     300,
    Last15Minutes  =     900,
    LastHour       =   3_600,
    LastSixHours   =  21_600,
    Last24Hours    =  86_400,
    FullSession    = int.MaxValue,
}

// ── Core telemetry record ──────────────────────────────────────────────────────

/// <summary>
/// All five hardware metrics captured at a single telemetry tick.
/// Built by <see cref="OperationalReplayService"/> from the five parallel history arrays.
/// </summary>
public sealed record HistoricalTelemetryPoint(
    DateTimeOffset Timestamp,
    double CpuPercent,
    double RamPercent,
    double GpuPercent,
    double DiskPercent,
    double TemperatureCelsius)
{
    /// <summary>True when the temperature sensor was reporting a plausible value at this tick.</summary>
    public bool HasTemperature => TemperatureCelsius > 0;
}

// ── Insight state reconstructed from timeline events ──────────────────────────

/// <summary>
/// A system insight that was active at some historical moment,
/// reconstructed from InsightOnset / InsightResolved timeline events.
/// </summary>
public sealed record ActiveInsightInfo(
    string                 InsightId,
    string                 Title,
    DateTimeOffset         OnsetAt,
    TimelineEventSeverity  Severity,
    IReadOnlyList<string>  AttributedProcesses);

// ── Point-in-time system state snapshot ───────────────────────────────────────

/// <summary>
/// Full reconstruction of the system's operational state at a specific point in time.
/// Produced by <see cref="IOperationalReplayService.ReconstructStateAt"/>.
/// </summary>
public sealed record SystemStateSnapshot(
    /// <summary>The time this snapshot represents.</summary>
    DateTimeOffset                    Timestamp,

    /// <summary>All five hardware metrics at (or nearest to) this timestamp.</summary>
    HistoricalTelemetryPoint          Telemetry,

    /// <summary>Insights that were active at this timestamp, reconstructed from the event stream.</summary>
    IReadOnlyList<ActiveInsightInfo>  ActiveInsights,

    /// <summary>All timeline events from session start up to and including this timestamp.</summary>
    IReadOnlyList<TimelineEvent>      EventsToNow,

    /// <summary>All completed (non-dry-run) actions executed up to this timestamp.</summary>
    IReadOnlyList<OperationRecord>    ActionsToNow,

    /// <summary>Human-readable health state label at this moment ("Healthy", "Degraded", etc.).</summary>
    string                            HealthStateLabel,

    /// <summary>Short narrative sentence describing the state at this moment.</summary>
    string                            NarrativeSummary,

    /// <summary>Number of telemetry data points available at or before this timestamp.</summary>
    int                               DataPointsAvailable);

// ── Causal chain ───────────────────────────────────────────────────────────────

/// <summary>What kind of event a causal chain link represents.</summary>
public enum CausalLinkKind
{
    /// <summary>A measurable shift in one or more hardware metrics preceded the onset.</summary>
    TelemetryShift,

    /// <summary>An intelligence rule fired and an insight became active.</summary>
    InsightOnset,

    /// <summary>A process was identified as a major contributor.</summary>
    ProcessEscalation,

    /// <summary>A forecast rule elevated its warning level.</summary>
    ForecastEscalation,

    /// <summary>A remediation action was executed by the user.</summary>
    ActionTaken,

    /// <summary>Conditions returned to normal — an insight resolved.</summary>
    Stabilization,

    /// <summary>The synthesis engine identified a cross-insight correlation.</summary>
    SynthesisCorrelation,
}

/// <summary>
/// A single step in a causal sequence of events that led to a system condition.
/// </summary>
public sealed record CausalChainLink(
    string                 EventTitle,
    string                 Description,
    DateTimeOffset         OccurredAt,
    CausalLinkKind         Kind,
    int                    ConfidencePercent,
    IReadOnlyList<string>  AttributedProcesses);

/// <summary>
/// A complete causal chain from initial signal through escalation and (optionally) resolution.
/// Built by <see cref="CausalChainAnalyzer"/> from the timeline event stream.
/// </summary>
public sealed record CausalChain(
    string                          Title,
    string                          Summary,
    IReadOnlyList<CausalChainLink>  Links,
    DateTimeOffset                  StartedAt,
    DateTimeOffset?                 ResolvedAt,
    bool                            IsResolved,
    int                             OverallConfidencePercent);

// ── Operational delta ──────────────────────────────────────────────────────────

/// <summary>
/// The measurable difference between two <see cref="SystemStateSnapshot"/> instances.
/// Produced by <see cref="OperationalDeltaAnalyzer"/>.
/// </summary>
public sealed record OperationalDelta(
    DateTimeOffset           FromTime,
    DateTimeOffset           ToTime,

    /// <summary>Positive = increased (worsened for CPU/RAM/Disk; improved if temperature dropped).</summary>
    double                   CpuChange,
    double                   RamChange,
    double                   GpuChange,
    double                   DiskChange,
    double                   TempChange,

    /// <summary>Insights that appeared between the two snapshots.</summary>
    IReadOnlyList<string>    NewInsights,

    /// <summary>Insights that resolved between the two snapshots.</summary>
    IReadOnlyList<string>    ResolvedInsights,

    /// <summary>Action titles executed between the two snapshots.</summary>
    IReadOnlyList<string>    NewActions,

    /// <summary>Human-readable summary lines describing the most notable changes.</summary>
    IReadOnlyList<string>    SummaryLines,

    /// <summary>True when at least one metric changed by > 5 % or an insight appeared/resolved.</summary>
    bool                     HasSignificantChange,

    /// <summary>Direction indicator: positive = overall worsening, negative = improvement.</summary>
    DeltaDirection           Direction);

/// <summary>Overall directionality of an operational delta.</summary>
public enum DeltaDirection { Improving, Stable, Worsening }

// ── Before/after remediation comparison ──────────────────────────────────────

/// <summary>
/// Comparison of system state before and after a remediation action.
/// Built by <see cref="IOperationalReplayService.BuildRemediationComparison"/>.
/// </summary>
public sealed record RemediationComparison(
    OperationRecord      Action,
    SystemStateSnapshot  SnapshotBefore,
    SystemStateSnapshot  SnapshotAfter,
    OperationalDelta     Delta,
    string               ComparisonNarrative,
    bool                 ShowsImprovement);

// ── Replay narratives ──────────────────────────────────────────────────────────

/// <summary>What kind of story a replay narrative is telling.</summary>
public enum ReplayNarrativeKind
{
    /// <summary>A summary of the full session so far.</summary>
    SessionSummary,

    /// <summary>An explanation of how conditions worsened.</summary>
    DegradationProgression,

    /// <summary>An explanation of how conditions improved.</summary>
    StabilizationExplanation,

    /// <summary>An explanation of how a remediation affected the system.</summary>
    RemediationEffect,
}

/// <summary>
/// Historical narrative prose describing a period of system operation.
/// </summary>
public sealed record ReplayNarrative(
    string               Title,
    string[]             Paragraphs,
    DateTimeOffset       CoveredFrom,
    DateTimeOffset       CoveredTo,
    ReplayNarrativeKind  Kind);

// ── Replay session ─────────────────────────────────────────────────────────────

/// <summary>
/// An immutable snapshot of all data available for replay, captured at the moment
/// the session was created. Contains the complete telemetry series, event stream,
/// and operation history for the covered window.
///
/// A new session is created each time the user opens the Replay page or refreshes.
/// Sessions are not persisted — they are rebuilt from live in-memory data.
/// (SQLite persistence will extend replay depth in a future phase.)
/// </summary>
public sealed record ReplaySession(
    string                                  Id,
    DateTimeOffset                          CreatedAt,

    /// <summary>Oldest data point available (governs the left edge of the scrubber).</summary>
    DateTimeOffset                          OldestDataPoint,

    /// <summary>Newest data point available (governs the right edge of the scrubber).</summary>
    DateTimeOffset                          NewestDataPoint,

    /// <summary>
    /// All telemetry samples captured during the session window, sorted oldest-first.
    /// Sourced from the 30-minute rolling telemetry history buffer.
    /// </summary>
    IReadOnlyList<HistoricalTelemetryPoint> TelemetrySeries,

    /// <summary>
    /// All timeline events available, sorted oldest-first.
    /// Sourced from the in-memory timeline aggregation service (500-event cap).
    /// </summary>
    IReadOnlyList<TimelineEvent>            Events,

    /// <summary>
    /// All operation records from persistent history, sorted oldest-first.
    /// Includes actions from previous sessions that were persisted to disk.
    /// </summary>
    IReadOnlyList<OperationRecord>          Actions,

    /// <summary>Current machine baseline — used for context in delta comparisons.</summary>
    MachineBaseline?                        Baseline,

    /// <summary>Total data points: telemetry samples + events + actions.</summary>
    int                                     TotalDataPoints)
{
    /// <summary>Duration of data coverage.</summary>
    public TimeSpan Coverage => NewestDataPoint - OldestDataPoint;

    /// <summary>True when the session has at least one telemetry sample.</summary>
    public bool HasTelemetry => TelemetrySeries.Count > 0;

    /// <summary>True when the session has at least one timeline event.</summary>
    public bool HasEvents => Events.Count > 0;

    /// <summary>True when any completed (non-dry-run) remediation actions exist.</summary>
    public bool HasActions => Actions.Any(a => a.IsSuccess && !a.IsDryRun);
}
