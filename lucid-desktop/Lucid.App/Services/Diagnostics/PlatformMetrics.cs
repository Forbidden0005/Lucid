namespace Lucid.Services.Diagnostics;

// ── Platform metrics models ───────────────────────────────────────────────────

/// <summary>
/// Immutable snapshot of platform health and performance metrics collected
/// by <see cref="IPlatformMetricsService"/> at a point in time.
///
/// Used by the Diagnostics page and the internal reasoning engine to
/// understand how Lucid itself is performing.
/// </summary>
public sealed record PlatformMetricsSnapshot
{
    /// <summary>UTC time this snapshot was taken.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    // ── Event bus ─────────────────────────────────────────────────────────────

    /// <summary>Total events published since platform start.</summary>
    public long TotalEventsPublished { get; init; }

    /// <summary>Events published in the last 60 seconds.</summary>
    public long EventsPerMinute { get; init; }

    // ── Execution engine ──────────────────────────────────────────────────────

    /// <summary>Total action executions attempted since platform start.</summary>
    public long TotalActionsExecuted { get; init; }

    /// <summary>Total action execution failures since platform start.</summary>
    public long TotalActionFailures { get; init; }

    /// <summary>
    /// Average action execution latency in milliseconds over the last 100 executions.
    /// </summary>
    public double AverageActionLatencyMs { get; init; }

    // ── Workflow engine ───────────────────────────────────────────────────────

    /// <summary>Total workflows started since platform start.</summary>
    public long TotalWorkflowsStarted { get; init; }

    /// <summary>Total workflows completed (successfully or not) since platform start.</summary>
    public long TotalWorkflowsCompleted { get; init; }

    /// <summary>Workflows currently executing.</summary>
    public int ActiveWorkflowCount { get; init; }

    // ── Exception tracking ────────────────────────────────────────────────────

    /// <summary>Total exceptions caught across all platform services since start.</summary>
    public long TotalExceptionsCaught { get; init; }

    /// <summary>Exceptions caught in the last 60 seconds.</summary>
    public long ExceptionsPerMinute { get; init; }

    // ── Memory ────────────────────────────────────────────────────────────────

    /// <summary>Current managed heap size in bytes.</summary>
    public long ManagedHeapBytes { get; init; }

    /// <summary>GC collection count across all generations since last sample.</summary>
    public int GcCollectionCount { get; init; }

    // ── Degraded mode ─────────────────────────────────────────────────────────

    /// <summary>Number of times the platform has entered degraded mode since start.</summary>
    public int DegradedModeTransitions { get; init; }

    /// <summary>Whether the platform is currently in degraded mode.</summary>
    public bool IsDegradedModeActive { get; init; }

    // ── Computed health score ─────────────────────────────────────────────────

    /// <summary>
    /// Platform self-health score 0–100, where 100 is fully healthy.
    /// Computed from failure rate, exception rate, and degraded mode status.
    /// </summary>
    public int PlatformHealthScore { get; init; }
}

/// <summary>
/// Counters accumulated by <see cref="IPlatformMetricsService"/> over time.
/// All fields are incremented atomically.
/// </summary>
public sealed class PlatformMetricsCounters
{
    public long TotalEventsPublished;
    public long TotalActionsExecuted;
    public long TotalActionFailures;
    public long TotalWorkflowsStarted;
    public long TotalWorkflowsCompleted;
    public long TotalExceptionsCaught;
    public int  DegradedModeTransitions;
}
