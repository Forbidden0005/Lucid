namespace ExplainMyPC.Services.Diagnostics;

/// <summary>
/// Public contract of the internal diagnostics service.
///
/// The diagnostics service is a passive observer: other services report
/// events to it, and it aggregates them into health records, event timelines,
/// and self-telemetry snapshots. It never drives any behavior itself.
///
/// All methods on this interface are fire-and-forget and never throw —
/// diagnostics failures must never affect the observed services.
///
/// Lifecycle: call <see cref="Start"/> and <see cref="Stop"/> to manage
/// the periodic self-monitoring timer.
/// </summary>
public interface IInternalDiagnosticsService
{
    // ── Reporting ─────────────────────────────────────────────────────────────

    /// <summary>Records a sampler result (success or failure).</summary>
    void RecordSamplerResult(
        string  samplerName,
        bool    success,
        double  durationMs = 0,
        string? errorMessage = null);

    /// <summary>Records an executor result (success or failure).</summary>
    void RecordExecutorResult(
        string  actionId,
        bool    success,
        string? errorDetail = null);

    /// <summary>Records a generic service-level event.</summary>
    void RecordServiceEvent(
        string               serviceName,
        DiagnosticsEventType eventType,
        string               message,
        DiagnosticSeverity   severity  = DiagnosticSeverity.Info,
        string?              detail    = null);

    /// <summary>Records a service heartbeat (success signal).</summary>
    void RecordServiceHeartbeat(string serviceName);

    /// <summary>Records a service failure.</summary>
    void RecordServiceFailure(string serviceName, string message);

    // ── Observation ───────────────────────────────────────────────────────────

    /// <summary>Returns a point-in-time self-telemetry snapshot.</summary>
    SelfTelemetrySnapshot GetSnapshot();

    /// <summary>Returns the most recent diagnostics events, newest-first.</summary>
    IReadOnlyList<DiagnosticsEvent> GetRecentEvents(int max = 100);

    /// <summary>Returns current health records for all monitored services.</summary>
    IReadOnlyDictionary<string, ServiceHealthRecord> GetServiceHealth();

    /// <summary>Returns health entries for all tracked telemetry samplers.</summary>
    IReadOnlyList<SamplerHealthEntry> GetSamplerHealth();

    /// <summary>Returns failure summaries for all tracked action executors.</summary>
    IReadOnlyList<ExecutorFailureSummary> GetExecutorSummaries();

    /// <summary>The current software-level degraded mode.</summary>
    DegradedMode CurrentDegradedMode { get; }

    /// <summary>The recovery coordinator — exposes actionable recovery operations.</summary>
    RecoveryCoordinator Recovery { get; }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the UI thread when the degraded mode changes.
    /// Used by the diagnostics ViewModel to update the mode banner.
    /// </summary>
    event EventHandler<DegradedModeChangedEventArgs>? DegradedModeChanged;

    /// <summary>
    /// Raised (on the reporting thread) when a new diagnostics event is appended.
    /// Subscribers must be cheap and non-blocking.
    /// </summary>
    event EventHandler<DiagnosticsEvent>? EventAppended;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>Starts the periodic self-monitoring timer.</summary>
    void Start();

    /// <summary>Stops the periodic self-monitoring timer.</summary>
    void Stop();
}
