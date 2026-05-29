namespace Lucid.Services.Diagnostics;

/// <summary>
/// Collects and exposes internal platform health metrics.
///
/// Services report notable events to this service so Lucid can reason
/// about its own operational health and surface self-diagnostics to the
/// Diagnostics page.
///
/// This is intentionally distinct from the user-facing system telemetry
/// (CPU/RAM/disk) collected by <see cref="Lucid.Services.ITelemetryService"/>.
/// Platform metrics are about Lucid itself, not the host machine.
///
/// Thread-safe: all reporting methods are safe for concurrent callers.
/// </summary>
public interface IPlatformMetricsService
{
    // ── Snapshot ──────────────────────────────────────────────────────────────

    /// <summary>Returns the current platform metrics snapshot.</summary>
    PlatformMetricsSnapshot GetSnapshot();

    // ── Event tracking ────────────────────────────────────────────────────────

    /// <summary>Called by the event bus after each successful event dispatch.</summary>
    void RecordEventPublished();

    // ── Execution tracking ────────────────────────────────────────────────────

    /// <summary>Called by the execution engine when an action starts.</summary>
    void RecordActionStarted();

    /// <summary>Called by the execution engine when an action completes.</summary>
    /// <param name="succeeded">Whether the action completed without error.</param>
    /// <param name="latencyMs">Elapsed time in milliseconds.</param>
    void RecordActionCompleted(bool succeeded, double latencyMs);

    // ── Workflow tracking ─────────────────────────────────────────────────────

    /// <summary>Called by the workflow engine when a workflow starts.</summary>
    void RecordWorkflowStarted();

    /// <summary>Called by the workflow engine when a workflow completes.</summary>
    void RecordWorkflowCompleted();

    // ── Exception tracking ────────────────────────────────────────────────────

    /// <summary>
    /// Called by any service when an exception is caught and handled.
    /// Do not call for expected exceptions (e.g. OperationCanceledException).
    /// </summary>
    void RecordExceptionCaught();

    // ── Degraded mode tracking ────────────────────────────────────────────────

    /// <summary>Called when the platform transitions into degraded mode.</summary>
    void RecordDegradedModeEntry();

    /// <summary>Called when the platform exits degraded mode.</summary>
    void RecordDegradedModeExit();
}
