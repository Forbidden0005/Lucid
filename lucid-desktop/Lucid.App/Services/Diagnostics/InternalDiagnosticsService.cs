using Lucid.Services.Governance;
using Lucid.Services.Persistence;
using Microsoft.UI.Dispatching;

namespace Lucid.Services.Diagnostics;

/// <summary>
/// Concrete implementation of <see cref="IInternalDiagnosticsService"/>.
///
/// Composes all diagnostics sub-components and provides the single entry
/// point for all internal health reporting.
///
/// Key design rules:
///   1. EVERY public method wraps its body in try/catch — diagnostics can never
///      crash the app, even if a sub-component has a bug.
///   2. All writes are synchronous and lock-protected — no async on the hot path.
///   3. After each failure record, the degraded-mode controller re-evaluates.
///   4. A periodic timer (every 30 seconds) takes a self-telemetry snapshot and
///      checks for memory pressure and queue buildup.
///
/// Services tracked:
///   Telemetry, Intelligence, Narrative, Timeline, Governance, Persistence
/// </summary>
public sealed class InternalDiagnosticsService : IInternalDiagnosticsService, IDisposable
{
    // ── Configuration ─────────────────────────────────────────────────────────

    private static readonly TimeSpan SelfMonitorInterval  = TimeSpan.FromSeconds(30);
    private const           int      PersistenceQueueWarn = 500;  // warn if queue > 500

    // ── Sub-components ────────────────────────────────────────────────────────

    private readonly DiagnosticsEventAggregator _events;
    private readonly ServiceHealthMonitor       _health;
    private readonly SamplerHealthTracker       _samplers;
    private readonly ExecutorFailureTracker     _executors;
    private readonly RuntimeAnomalyDetector     _anomalies;
    private readonly DegradedModeController     _degraded;
    private readonly RecoveryCoordinator        _recovery;

    // ── External references ───────────────────────────────────────────────────

    private readonly SQLitePersistenceService? _persistence;
    private readonly ConcurrencyBudget?        _budget;
    private readonly DispatcherQueue           _uiDispatcher;

    // ── State ─────────────────────────────────────────────────────────────────

    private System.Threading.Timer? _selfMonitorTimer;
    private bool                    _disposed;

    // ── Events ────────────────────────────────────────────────────────────────

    public event EventHandler<DegradedModeChangedEventArgs>? DegradedModeChanged;
    public event EventHandler<DiagnosticsEvent>?             EventAppended;

    // ── Construction ──────────────────────────────────────────────────────────

    public InternalDiagnosticsService(
        DispatcherQueue            uiDispatcher,
        SQLitePersistenceService?  persistence  = null,
        ConcurrencyBudget?         budget       = null,
        PollingCoordinator?        polling      = null,
        ExecutionPriorityQueue?    queue        = null)
    {
        _uiDispatcher = uiDispatcher;
        _persistence  = persistence;
        _budget       = budget;

        _events    = new DiagnosticsEventAggregator();
        _health    = new ServiceHealthMonitor();
        _samplers  = new SamplerHealthTracker();
        _executors = new ExecutorFailureTracker();
        _anomalies = new RuntimeAnomalyDetector();
        _degraded  = new DegradedModeController();
        _recovery  = new RecoveryCoordinator(_events, persistence, budget, polling, queue);

        // Forward aggregator events to our own event
        _events.EventAppended += (_, ev) =>
        {
            try { EventAppended?.Invoke(this, ev); }
            catch { /* best-effort */ }
        };

        // Forward mode changes (dispatched to UI thread)
        _degraded.ModeChanged += (_, args) =>
        {
            // Post a self-describing event first
            RecordDegradedModeEvent(args);

            _uiDispatcher.TryEnqueue(() =>
            {
                try { DegradedModeChanged?.Invoke(this, args); }
                catch { /* best-effort */ }
            });
        };

        // Register all known services so the health dashboard is populated
        // immediately, even before any heartbeats arrive.
        RegisterKnownServices();
    }

    // ── IInternalDiagnosticsService — reporting ───────────────────────────────

    public void RecordSamplerResult(
        string  samplerName,
        bool    success,
        double  durationMs   = 0,
        string? errorMessage = null)
    {
        try
        {
            if (success)
            {
                _samplers.RecordSuccess(samplerName, durationMs);
            }
            else
            {
                _samplers.RecordFailure(samplerName, errorMessage);
                _events.Append(
                    DiagnosticsEventType.SamplerFailure,
                    DiagnosticSeverity.Warning,
                    $"Sampler:{samplerName}",
                    $"{samplerName} sampler reported a failure.",
                    errorMessage);

                EvaluateDegradedMode();
            }
        }
        catch { /* diagnostics must never crash the caller */ }
    }

    public void RecordExecutorResult(
        string  actionId,
        bool    success,
        string? errorDetail = null)
    {
        try
        {
            if (success)
            {
                _executors.RecordSuccess(actionId);
                _health.RecordSuccess("ActionExecutionEngine");
            }
            else
            {
                _executors.RecordFailure(actionId, errorDetail);
                _health.RecordFailure("ActionExecutionEngine",
                    $"Executor '{actionId}' failed: {errorDetail}");

                _events.Append(
                    DiagnosticsEventType.ExecutorCrash,
                    DiagnosticSeverity.Warning,
                    "ActionExecutionEngine",
                    $"Executor '{actionId}' failed.",
                    errorDetail);

                EvaluateDegradedMode();
            }
        }
        catch { /* best-effort */ }
    }

    public void RecordServiceEvent(
        string               serviceName,
        DiagnosticsEventType eventType,
        string               message,
        DiagnosticSeverity   severity = DiagnosticSeverity.Info,
        string?              detail   = null)
    {
        try
        {
            _events.Append(eventType, severity, serviceName, message, detail);

            if (severity == DiagnosticSeverity.Critical ||
                eventType == DiagnosticsEventType.ServiceDegraded)
            {
                _health.RecordFailure(serviceName, message);
                EvaluateDegradedMode();
            }
        }
        catch { /* best-effort */ }
    }

    public void RecordServiceHeartbeat(string serviceName)
    {
        try { _health.RecordSuccess(serviceName); }
        catch { /* best-effort */ }
    }

    public void RecordServiceFailure(string serviceName, string message)
    {
        try
        {
            _health.RecordFailure(serviceName, message);
            _events.Append(
                DiagnosticsEventType.ServiceDegraded,
                DiagnosticSeverity.Warning,
                serviceName,
                message);
            EvaluateDegradedMode();
        }
        catch { /* best-effort */ }
    }

    // ── Telemetry / anomaly integration ───────────────────────────────────────

    /// <summary>
    /// Called by AppServices on each ReadingAvailable event.
    /// Checks for polling overruns; also marks the telemetry service healthy.
    /// </summary>
    public void OnTelemetryReceived(DateTimeOffset timestamp, TimeSpan currentInterval)
    {
        try
        {
            _health.RecordSuccess("WindowsTelemetryService");
            _anomalies.SetExpectedInterval(currentInterval);

            var overrun = _anomalies.OnTelemetryReceived(timestamp, currentInterval);
            if (overrun is not null)
            {
                _events.Append(
                    DiagnosticsEventType.PollingOverrun,
                    overrun.Severity,
                    "WindowsTelemetryService",
                    overrun.Message);
            }
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Called when the governance mode changes.
    /// Records the transition and checks for oscillation.
    /// </summary>
    public void OnGovernanceModeChanged(
        Lucid.Services.Governance.RuntimeMode previous,
        Lucid.Services.Governance.RuntimeMode next,
        Lucid.Services.Governance.ThrottlingReason reasons)
    {
        try
        {
            var description = RuntimePressureAnalyzer.DescribeReasons(reasons);
            _events.Append(
                DiagnosticsEventType.ModeTransition,
                next == Lucid.Services.Governance.RuntimeMode.Normal
                    ? DiagnosticSeverity.Info
                    : DiagnosticSeverity.Warning,
                "RuntimeGovernanceService",
                $"Runtime mode: {RuntimePressureAnalyzer.DescribeMode(previous)} → " +
                $"{RuntimePressureAnalyzer.DescribeMode(next)}",
                description);

            _health.RecordSuccess("RuntimeGovernanceService");
            _anomalies.SetExpectedInterval(
                AdaptiveSchedulingPolicy.GetTelemetryInterval(next));

            var oscillation = _anomalies.OnGovernanceModeChanged(DateTimeOffset.Now);
            if (oscillation is not null)
            {
                _events.Append(
                    DiagnosticsEventType.PollingOverrun,
                    oscillation.Severity,
                    "RuntimeGovernanceService",
                    oscillation.Message);
            }
        }
        catch { /* best-effort */ }
    }

    // ── IInternalDiagnosticsService — observation ─────────────────────────────

    public SelfTelemetrySnapshot GetSnapshot()
    {
        try
        {
            return new SelfTelemetrySnapshot(
                ProcessWorkingSetBytes: Environment.WorkingSet,
                ManagedHeapBytes:       GC.GetTotalMemory(false),
                ActiveWorkloadCount:    _budget?.GetActiveWorkloads().Count ?? 0,
                DeferredWorkloadCount:  0,   // queue not directly visible here
                PersistenceQueueDepth:  _persistence?.PendingWriteCount ?? 0,
                DiagnosticsEventCount:  _events.Count,
                PersistenceIsHealthy:   _persistence?.IsHealthy ?? false,
                CurrentDegradedMode:    _degraded.Current,
                SnapshotAt:             DateTimeOffset.Now);
        }
        catch
        {
            return new SelfTelemetrySnapshot(0, 0, 0, 0, 0, 0, false,
                DegradedMode.Full, DateTimeOffset.Now);
        }
    }

    public IReadOnlyList<DiagnosticsEvent>              GetRecentEvents(int max = 100) =>
        _events.GetRecent(max);

    public IReadOnlyDictionary<string, ServiceHealthRecord> GetServiceHealth() =>
        _health.GetAll();

    public IReadOnlyList<SamplerHealthEntry>            GetSamplerHealth()    =>
        _samplers.GetAll();

    public IReadOnlyList<ExecutorFailureSummary>        GetExecutorSummaries() =>
        _executors.GetAll();

    public DegradedMode      CurrentDegradedMode => _degraded.Current;
    public RecoveryCoordinator Recovery           => _recovery;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Start()
    {
        if (_disposed) return;
        _selfMonitorTimer = new System.Threading.Timer(
            _ => SelfMonitorTick(),
            null,
            SelfMonitorInterval,
            SelfMonitorInterval);

        _events.Append(
            DiagnosticsEventType.ServiceStarted,
            DiagnosticSeverity.Info,
            "InternalDiagnosticsService",
            "Internal diagnostics service started.");
    }

    public void Stop()
    {
        _selfMonitorTimer?.Dispose();
        _selfMonitorTimer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void SelfMonitorTick()
    {
        try
        {
            // Memory pressure check
            var memAnomaly = _anomalies.CheckMemoryPressure();
            if (memAnomaly is not null)
            {
                _events.Append(
                    DiagnosticsEventType.MemoryPressure,
                    memAnomaly.Severity,
                    "InternalDiagnosticsService",
                    memAnomaly.Message);
            }

            // Persistence queue pressure check
            if (_persistence is { IsHealthy: true } p)
            {
                int qDepth = p.PendingWriteCount;
                if (qDepth > PersistenceQueueWarn)
                {
                    _events.Append(
                        DiagnosticsEventType.QueuePressure,
                        DiagnosticSeverity.Warning,
                        "SQLitePersistenceService",
                        $"Persistence queue depth is {qDepth} — may indicate a write stall.",
                        $"Threshold: {PersistenceQueueWarn}");
                }

                _health.RecordSuccess("SQLitePersistenceService");
            }
            else if (_persistence is { IsHealthy: false })
            {
                _health.RecordFailure("SQLitePersistenceService",
                    "SQLite database is not healthy.");
            }

            // Re-evaluate degraded mode
            EvaluateDegradedMode();
        }
        catch { /* self-monitor must never crash the app */ }
    }

    private void EvaluateDegradedMode()
    {
        try { _degraded.EvaluateAndTransition(_health, _samplers); }
        catch { /* best-effort */ }
    }

    private void RecordDegradedModeEvent(DegradedModeChangedEventArgs args)
    {
        try
        {
            _events.Append(
                args.IsEscalation
                    ? DiagnosticsEventType.DegradedModeEntered
                    : DiagnosticsEventType.DegradedModeExited,
                args.IsEscalation ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info,
                "DegradedModeController",
                args.Description);
        }
        catch { /* best-effort */ }
    }

    private void RegisterKnownServices()
    {
        foreach (var name in new[]
        {
            "WindowsTelemetryService",
            "SystemInsightEngine",
            "OperationalNarrativeEngine",
            "TimelineAggregationService",
            "RuntimeGovernanceService",
            "SQLitePersistenceService",
            "ActionExecutionEngine",
        })
        {
            _health.Register(name);
        }
    }
}
