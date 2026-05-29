namespace Lucid.Services.Infrastructure.OperationalState;

/// <summary>
/// Immutable point-in-time snapshot of the Lucid platform's unified
/// operational state.
///
/// Produced by <see cref="IOperationalStateCoordinator"/> and consumed by
/// UI ViewModels, the intelligence engine, governance, and the LLM context
/// builder to give every subsystem a consistent view of platform posture.
///
/// Thread-safe: records are immutable — share freely across threads.
/// </summary>
public sealed record OperationalStateSnapshot
{
    /// <summary>UTC timestamp when this snapshot was produced.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    // ── Platform mode & conditions ────────────────────────────────────────────

    /// <summary>High-level operational mode of the platform.</summary>
    public OperationalMode Mode { get; init; } = OperationalMode.Normal;

    /// <summary>Bitmask of currently active runtime conditions.</summary>
    public RuntimeCondition ActiveConditions { get; init; } = RuntimeCondition.None;

    /// <summary>Computed attention level for UI notification prominence.</summary>
    public SystemAttentionLevel AttentionLevel { get; init; } = SystemAttentionLevel.Normal;

    // ── Workload ──────────────────────────────────────────────────────────────

    /// <summary>Count of active workflows currently executing.</summary>
    public int ActiveWorkflowCount { get; init; }

    /// <summary>Identifiers of the currently active workflow instances.</summary>
    public IReadOnlyList<string> ActiveWorkflowIds { get; init; } = [];

    // ── Trust & consent ───────────────────────────────────────────────────────

    /// <summary>True if any consent request is currently awaiting user response.</summary>
    public bool ConsentPending { get; init; }

    /// <summary>
    /// Description of the pending consent request, or null if none is active.
    /// </summary>
    public string? PendingConsentDescription { get; init; }

    // ── Service health ────────────────────────────────────────────────────────

    /// <summary>Count of services currently in degraded state.</summary>
    public int DegradedServiceCount { get; init; }

    /// <summary>Count of services currently in failed state.</summary>
    public int FailedServiceCount { get; init; }

    // ── Runtime pressure ──────────────────────────────────────────────────────

    /// <summary>Latest CPU utilization percentage (0–100).</summary>
    public double CpuPercent { get; init; }

    /// <summary>Latest RAM utilization percentage (0–100).</summary>
    public double RamPercent { get; init; }

    /// <summary>True if battery-aware mode is currently active.</summary>
    public bool BatteryAwareMode { get; init; }

    // ── Activity flags ────────────────────────────────────────────────────────

    /// <summary>True if an operational simulation is currently running.</summary>
    public bool SimulationRunning { get; init; }

    /// <summary>True if the companion conversation session is processing a turn.</summary>
    public bool ConversationActive { get; init; }

    /// <summary>True if a background automation sequence is executing.</summary>
    public bool AutomationActive { get; init; }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if a specific runtime condition is currently active.
    /// </summary>
    public bool HasCondition(RuntimeCondition condition) =>
        (ActiveConditions & condition) != 0;

    /// <summary>Returns an empty snapshot representing a freshly started platform.</summary>
    public static OperationalStateSnapshot Empty => new();
}
