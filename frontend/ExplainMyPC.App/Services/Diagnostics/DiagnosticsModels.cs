namespace ExplainMyPC.Services.Diagnostics;

// ── Service health ─────────────────────────────────────────────────────────────

/// <summary>Operational status of a monitored internal service.</summary>
public enum ServiceHealthStatus
{
    /// <summary>Service is operating normally with no recent failures.</summary>
    Healthy   = 0,

    /// <summary>Service experienced recent failures but is still functioning.</summary>
    Degraded  = 1,

    /// <summary>Service is not responding or has exceeded failure thresholds.</summary>
    Failed    = 2,

    /// <summary>A recovery attempt is in progress.</summary>
    Recovering = 3,

    /// <summary>Service state has not been observed yet (cold start).</summary>
    Unknown   = 4,
}

// ── Severity ──────────────────────────────────────────────────────────────────

/// <summary>Severity tier for a diagnostics event.</summary>
public enum DiagnosticSeverity
{
    Info     = 0,
    Warning  = 1,
    Critical = 2,
}

// ── Event types ───────────────────────────────────────────────────────────────

/// <summary>Category of a diagnostics event.</summary>
public enum DiagnosticsEventType
{
    ServiceStarted       = 0,
    ServiceStopped       = 1,
    ServiceDegraded      = 2,
    ServiceRecovered     = 3,
    SamplerFailure       = 4,
    SamplerRecovered     = 5,
    ExecutorCrash        = 6,
    RecoveryAttempt      = 7,
    RecoverySucceeded    = 8,
    RecoveryFailed       = 9,
    ModeTransition       = 10,
    QueuePressure        = 11,
    PollingOverrun       = 12,
    MemoryPressure       = 13,
    PersistenceStall     = 14,
    DegradedModeEntered  = 15,
    DegradedModeExited   = 16,
}

// ── Degraded mode ─────────────────────────────────────────────────────────────

/// <summary>
/// Software-level operational mode of the diagnostics layer.
/// Layered on top of the hardware-driven <see cref="RuntimeMode"/>:
/// hardware pressure drives telemetry rate; software failures drive this.
/// </summary>
public enum DegradedMode
{
    /// <summary>All subsystems healthy — full feature set available.</summary>
    Full             = 0,

    /// <summary>
    /// One or more samplers intermittently failing.
    /// Telemetry polling continues at reduced rate; heavy analysis deferred.
    /// </summary>
    ReducedTelemetry = 1,

    /// <summary>
    /// Multiple subsystems degraded. Only critical-path telemetry active.
    /// Heavy analysis, narrative generation, and learning paused.
    /// </summary>
    MinimalMonitoring = 2,

    /// <summary>
    /// Severe platform instability. All background work halted.
    /// Only recovery operations and basic governance are allowed.
    /// </summary>
    RecoveryOnly     = 3,
}

// ── Records ───────────────────────────────────────────────────────────────────

/// <summary>Point-in-time health snapshot for a monitored service.</summary>
public sealed record ServiceHealthRecord(
    string              ServiceName,
    ServiceHealthStatus Status,
    DateTimeOffset      StartedAt,
    DateTimeOffset      LastSuccessAt,
    DateTimeOffset?     LastFailureAt,
    int                 FailureCount,
    int                 ConsecutiveFailures,
    int                 RecoveryAttempts,
    string?             LastFailureMessage)
{
    /// <summary>How long this service has been running.</summary>
    public TimeSpan Uptime => DateTimeOffset.Now - StartedAt;

    /// <summary>Human-readable status label.</summary>
    public string StatusLabel => Status switch
    {
        ServiceHealthStatus.Healthy    => "Healthy",
        ServiceHealthStatus.Degraded   => "Degraded",
        ServiceHealthStatus.Failed     => "Failed",
        ServiceHealthStatus.Recovering => "Recovering",
        _                              => "Unknown",
    };
}

/// <summary>A single event produced by the diagnostics layer.</summary>
public sealed record DiagnosticsEvent(
    DiagnosticsEventType Type,
    DiagnosticSeverity   Severity,
    string               ServiceName,
    string               Message,
    DateTimeOffset       OccurredAt,
    string?              Detail = null);

/// <summary>
/// Health entry for a single hardware telemetry sampler.
/// Samplers run inside <see cref="WindowsTelemetryService"/>.
/// </summary>
public sealed record SamplerHealthEntry(
    string         Name,
    int            SuccessCount,
    int            FailureCount,
    int            ConsecutiveFailures,
    DateTimeOffset LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    double         AvgDurationMs)
{
    /// <summary>True when the sampler has failed more than it has succeeded recently.</summary>
    public bool IsUnhealthy => ConsecutiveFailures >= 3;
}

/// <summary>Failure summary for a single action executor.</summary>
public sealed record ExecutorFailureSummary(
    string         ActionId,
    int            TotalExecutions,
    int            TotalFailures,
    int            ConsecutiveFailures,
    DateTimeOffset? LastFailureAt,
    string?        LastFailureDetail)
{
    /// <summary>Failure rate over lifetime (0.0–1.0).</summary>
    public double FailureRate => TotalExecutions > 0
        ? (double)TotalFailures / TotalExecutions
        : 0.0;
}

/// <summary>A recovery action available through the <see cref="RecoveryCoordinator"/>.</summary>
public sealed record RecoveryAction(
    string ActionId,
    string Label,
    string Description,
    bool   IsDestructive = false);

/// <summary>Result of executing a recovery action.</summary>
public sealed record RecoveryResult(
    string ActionId,
    bool   Succeeded,
    string Message,
    string? Detail = null);

/// <summary>
/// Point-in-time self-telemetry snapshot — ExplainMyPC observing its own
/// operational metrics.
/// </summary>
public sealed record SelfTelemetrySnapshot(
    long           ProcessWorkingSetBytes,
    long           ManagedHeapBytes,
    int            ActiveWorkloadCount,
    int            DeferredWorkloadCount,
    int            PersistenceQueueDepth,
    int            DiagnosticsEventCount,
    bool           PersistenceIsHealthy,
    DegradedMode   CurrentDegradedMode,
    DateTimeOffset SnapshotAt)
{
    public double WorkingSetMb => ProcessWorkingSetBytes / 1_048_576.0;
    public double HeapMb       => ManagedHeapBytes       / 1_048_576.0;
}
