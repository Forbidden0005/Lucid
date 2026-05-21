namespace ExplainMyPC.Services.Behavior;

// ── Enumerations ───────────────────────────────────────────────────────────────

/// <summary>High-level classification of the workload running on the machine.</summary>
public enum WorkloadType
{
    Unknown              = 0,
    Idle                 = 1,
    Gaming               = 2,
    Development          = 3,
    MediaEditing         = 4,
    Rendering            = 5,
    Streaming            = 6,
    BrowserResearch      = 7,
    Productivity         = 8,
    BackgroundProcessing = 9,
    StartupHeavy         = 10,
    ThermalIntensive     = 11,
    OvernightProcessing  = 12,
}

/// <summary>Confidence level for a behavioral classification or detected pattern.</summary>
public enum BehaviorConfidence { Low = 0, Medium = 1, High = 2 }

// ── Classification records ─────────────────────────────────────────────────────

/// <summary>
/// Result of classifying the current system workload.
/// All fields are deterministic — no probabilistic models, no ML.
/// </summary>
public sealed record WorkloadClassification(
    WorkloadType               PrimaryWorkload,
    WorkloadType               SecondaryWorkload,
    BehaviorConfidence         Confidence,
    string                     Rationale,
    DateTimeOffset             ClassifiedAt,
    IReadOnlyList<string>      ContributingSignals);

/// <summary>Records a transition between two committed workload types.</summary>
public sealed record WorkloadTransition(
    WorkloadType   FromWorkload,
    WorkloadType   ToWorkload,
    DateTimeOffset OccurredAt,
    TimeSpan       DurationInPreviousWorkload);

/// <summary>Behavioral summary of a single completed session.</summary>
public sealed record SessionSummary(
    string         SessionId,
    WorkloadType   DominantWorkload,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    double         PeakCpuPct,
    double         PeakRamPct,
    double         PeakThermalC,
    bool           HadThermalStress,
    bool           HadMemoryPressure,
    int            TransitionCount)
{
    public TimeSpan Duration => EndedAt - StartedAt;
}

/// <summary>
/// A recurring behavioral pattern detected across multiple sessions.
/// Patterns are observations — they never drive automated actions.
/// </summary>
public sealed record BehavioralPattern(
    string         PatternId,
    string         Label,
    string         Description,
    WorkloadType   AssociatedWorkload,
    int            ObservationCount,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt,
    BehaviorConfidence Confidence);

/// <summary>
/// Long-term behavioral identity profile for this specific machine.
/// Answers: "What kind of machine is this?"
/// </summary>
public sealed record MachineIdentityProfile(
    WorkloadType DominantWorkload,
    WorkloadType SecondaryWorkload,
    IReadOnlyList<(WorkloadType Type, int Sessions, double SharePct)> WorkloadBreakdown,
    string         IdentityNarrative,
    DateTimeOffset ProfiledAt,
    int            TotalSessionsAnalyzed,
    BehaviorConfidence Confidence);

/// <summary>
/// Point-in-time fingerprint of observable resource usage.
/// Captures raw numeric signals used for classification.
/// </summary>
public sealed record UsageFingerprint(
    double         AvgCpuPct,
    double         AvgRamPct,
    double         AvgThermalC,
    int            ActiveProcessCount,
    int            BrowserProcessCount,
    int            DevToolProcessCount,
    int            GameProcessCount,
    int            MediaProcessCount,
    double         GpuPercent,
    DateTimeOffset CapturedAt);

/// <summary>A recommendation enriched with behavioral workload context.</summary>
public sealed record ContextualRecommendation(
    string             ActionId,
    string             Title,
    string             ContextualRationale,
    BehaviorConfidence Confidence,
    bool               IsTimingAppropriate,
    string?            DeferralReason);

/// <summary>
/// Full behavioral context snapshot consumed by other engines.
/// Immutable — safe to pass across threads.
/// </summary>
public sealed record BehavioralContext(
    WorkloadClassification           CurrentWorkload,
    MachineIdentityProfile           MachineIdentity,
    IReadOnlyList<BehavioralPattern> ActivePatterns,
    IReadOnlyList<SessionSummary>    RecentSessions,
    UsageFingerprint?                CurrentFingerprint,
    DateTimeOffset                   ContextAt);
