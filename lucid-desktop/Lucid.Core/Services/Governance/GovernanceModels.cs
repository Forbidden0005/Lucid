namespace Lucid.Services.Governance;

// ── Work classification ────────────────────────────────────────────────────────

/// <summary>
/// Operational priority class for a unit of work.
///
/// The runtime governance service uses this to decide whether work can start now,
/// should be deferred, or must wait until the system is truly idle.
///
/// FOREGROUND  — user-initiated, time-sensitive. Never deferred.
/// BACKGROUND  — scheduled or passive. Deferred when the system is under stress.
/// IDLE_ONLY   — heavy, low-urgency scans. Only allowed when the system is calm.
/// </summary>
public enum WorkloadPriority
{
    Foreground = 0,
    Background = 1,
    IdleOnly   = 2,
}

/// <summary>
/// Logical category of a unit of work.
/// Used by <see cref="WorkloadClassifier"/> to determine priority and by
/// <see cref="ConcurrencyBudget"/> to enforce per-category concurrency limits.
/// </summary>
public enum WorkloadCategory
{
    // ── Foreground-class: always allowed, never deferred ──────────────────
    ActionExecution     = 0,   // repair/remediation executor
    ExplainReasoning    = 1,   // Lucid flagship reasoning pass
    ReplayAnalysis      = 2,   // operational replay reconstruction
    SecurityScan        = 3,   // SecurityIntelligenceService scan (user-initiated)
    ReliabilityAnalysis = 4,   // Windows event log read + crash correlation (user asked a question)

    // ── Background-class: yield to foreground, pause under stress ─────────
    TelemetrySampling   = 10,  // WindowsTelemetryService poll cycle
    ProcessIntelligence = 11,  // ProcessIntelligenceService scan
    NarrativeGeneration = 12,  // OperationalNarrativeEngine synthesis
    TimelineAggregation = 13,  // TimelineAggregationService event append
    InsightAnalysis     = 14,  // SystemInsightEngine rule evaluation

    // ── IdleOnly-class: only when system load is low ───────────────────────
    StorageScan         = 20,  // StorageAnalysisService full-tree traversal
    DuplicateHashing    = 21,  // SHA-256 duplicate detection hashing
    HistoricalAnalytics = 22,  // HistoricalAnalyticsEngine.ComputeAsync
    LearningAnalysis    = 23,  // RemediationLearningService.AnalyzePendingActionsAsync
    RollbackMaintenance = 24,  // RollbackStagingMaintenanceService expired-set sweep
    TelemetryRetention  = 25,  // TelemetryRetentionService hourly downsample + purge
}

// ── Runtime mode ──────────────────────────────────────────────────────────────

/// <summary>
/// Current operational mode of the runtime governance layer.
///
/// The mode determines polling rates, background concurrency limits, and
/// which workloads are allowed to start.
/// </summary>
public enum RuntimeMode
{
    /// <summary>Normal operation — all workloads allowed.</summary>
    Normal            = 0,

    /// <summary>Battery discharging — polling slowed, background work paused.</summary>
    LowPower          = 1,

    /// <summary>High GPU + sustained CPU — storage scans and hashing deferred.</summary>
    Gaming            = 2,

    /// <summary>CPU temperature critical — background work halted, telemetry slowed.</summary>
    ThermalProtection = 3,

    /// <summary>CPU ≥ high threshold — background concurrency reduced.</summary>
    HighLoad          = 4,
}

// ── Throttling reasons ────────────────────────────────────────────────────────

/// <summary>
/// Bit flags describing why the governance layer entered a non-Normal mode.
/// Multiple reasons can be active simultaneously.
/// </summary>
[Flags]
public enum ThrottlingReason
{
    None           = 0,
    CpuPressure    = 1 << 0,
    ThermalStress  = 1 << 1,
    BatteryMode    = 1 << 2,
    GamingDetected = 1 << 3,
    DiskPressure   = 1 << 4,
}

// ── Snapshot / tracking types ─────────────────────────────────────────────────

/// <summary>
/// Point-in-time snapshot of the resource governance budget.
/// Produced by <see cref="ConcurrencyBudget.GetSnapshot"/>.
/// </summary>
public sealed record ResourceBudgetSnapshot(
    RuntimeMode      Mode,
    ThrottlingReason Reasons,
    int              ActiveWorkloadCount,
    int              MaxConcurrentBackground,
    int              UsedBackgroundSlots,
    TimeSpan         TelemetryInterval,
    TimeSpan         ProcessSampleInterval,
    DateTimeOffset   SnapshotAt);

/// <summary>
/// A workload that is currently executing — tracked for observability.
/// </summary>
public sealed record ActiveWorkloadEntry(
    string           Name,
    WorkloadCategory Category,
    WorkloadPriority Priority,
    DateTimeOffset   StartedAt);

/// <summary>
/// A workload that was denied a budget slot and deferred.
/// Shown in the Runtime Governance page.
/// </summary>
public sealed record DeferredWorkloadEntry(
    string           Name,
    WorkloadCategory Category,
    WorkloadPriority Priority,
    DateTimeOffset   DeferredAt,
    string           DeferralReason);
