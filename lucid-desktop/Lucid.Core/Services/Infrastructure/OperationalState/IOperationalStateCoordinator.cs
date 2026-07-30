namespace Lucid.Services.Infrastructure.OperationalState;

/// <summary>
/// Maintains a unified, authoritative snapshot of the Lucid platform's
/// operational state by aggregating signals from all active subsystems.
///
/// Consumers:
/// - UI ViewModels: read <see cref="Current"/> to update status badges
/// - Intelligence engine: adjusts thresholds based on platform posture
/// - LLM context builder: injects operational state into system prompts
/// - Governance: reads workload and pressure to throttle background work
///
/// Publishers:
/// - Telemetry service: CPU/RAM/pressure updates
/// - Workflow engine: active workflow count changes
/// - Trust system: consent pending flags
/// - Diagnostics: service health changes
/// - Simulation engine: simulation active flag
///
/// Thread-safe: all methods are safe for concurrent callers.
/// </summary>
public interface IOperationalStateCoordinator
{
    /// <summary>
    /// The most recent operational state snapshot.
    /// Updated atomically — always consistent, never partially written.
    /// </summary>
    OperationalStateSnapshot Current { get; }

    /// <summary>
    /// Raised whenever the operational state snapshot is updated.
    /// Invoked on the thread pool — marshal to UI thread if needed.
    /// </summary>
    event Action<OperationalStateSnapshot>? StateChanged;

    // ── Condition reporting ───────────────────────────────────────────────────

    /// <summary>
    /// Sets a runtime condition flag as active. No-op if already set.
    /// Triggers a snapshot update.
    /// </summary>
    void SetCondition(RuntimeCondition condition);

    /// <summary>
    /// Clears a runtime condition flag. No-op if not set.
    /// Triggers a snapshot update.
    /// </summary>
    void ClearCondition(RuntimeCondition condition);

    // ── Mode transitions ──────────────────────────────────────────────────────

    /// <summary>
    /// Transitions the platform to the given operational mode.
    /// Triggers a snapshot update.
    /// </summary>
    void SetMode(OperationalMode mode);

    // ── Resource reporting ────────────────────────────────────────────────────

    /// <summary>
    /// Updates the latest CPU and RAM utilization percentages.
    /// Called by the telemetry service after each sample cycle.
    /// </summary>
    void UpdateResourcePressure(double cpuPercent, double ramPercent);

    // ── Workflow tracking ─────────────────────────────────────────────────────

    /// <summary>Registers a workflow as active. Sets <see cref="RuntimeCondition.WorkflowActive"/>.</summary>
    void WorkflowStarted(string workflowId);

    /// <summary>Removes a workflow from the active set. Clears the flag if no workflows remain.</summary>
    void WorkflowCompleted(string workflowId);

    // ── Consent tracking ──────────────────────────────────────────────────────

    /// <summary>
    /// Records that a consent request is pending, blocking the described operation.
    /// </summary>
    void ConsentRequested(string operationId, string description);

    /// <summary>Clears the pending consent state for the given operation.</summary>
    void ConsentResolved(string operationId);
}
