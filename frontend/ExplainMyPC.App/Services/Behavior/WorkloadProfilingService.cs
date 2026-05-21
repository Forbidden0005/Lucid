using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Session;

namespace ExplainMyPC.Services.Behavior;

/// <summary>
/// Main coordinator for machine behavioral profiling.
///
/// Subscribes to the telemetry feed and periodically:
///   1. Classifies the current workload (rate-limited to every 30 seconds)
///   2. Debounces workload transitions via <see cref="WorkloadTransitionTracker"/>
///   3. Tracks session-level peaks (CPU, RAM, thermal stress, memory pressure)
///   4. Accumulates completed sessions into <see cref="MachineBehaviorModel"/>
///   5. Rebuilds the identity profile and pattern list after each session flush
///
/// Design principles:
///   • Lightweight — classification runs in-memory with O(n-processes) work
///   • Passive — never drives actions; only observes and reports
///   • Self-contained — no database, no cloud, no external dependencies
///   • Fail-safe — every handler is wrapped in try/catch; never crashes the app
///
/// Threading:
///   OnReading fires on the telemetry thread (not UI thread).
///   <see cref="WorkloadChanged"/> is fired from that same thread.
///   ViewModel subscribers must dispatch to the UI thread.
/// </summary>
public sealed class WorkloadProfilingService : IDisposable
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>How often to run workload classification (seconds).</summary>
    private const double ClassificationIntervalSeconds = 30.0;

    /// <summary>Temperature threshold for flagging a session as thermal-stressed.</summary>
    private const double ThermalStressThresholdC = 85.0;

    /// <summary>RAM% threshold for flagging a session as memory-pressured.</summary>
    private const double MemoryPressureThresholdPct = 80.0;

    /// <summary>Minimum session duration before it is recorded in the model.</summary>
    private static readonly TimeSpan MinSessionDuration = TimeSpan.FromMinutes(2);

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly ITelemetryService      _telemetry;
    private readonly ISessionContextService? _session;

    // ── Sub-components ─────────────────────────────────────────────────────────

    private readonly WorkloadTransitionTracker _transitions = new();
    private readonly MachineBehaviorModel      _model       = new();

    // ── Classification rate-limit ─────────────────────────────────────────────

    private DateTimeOffset _lastClassifiedAt = DateTimeOffset.MinValue;

    // ── Current live session peaks ─────────────────────────────────────────────

    private double _peakCpu      = 0;
    private double _peakRam      = 0;
    private double _peakThermal  = 0;
    private bool   _hadThermal   = false;
    private bool   _hadMemory    = false;
    private int    _transitionCt = 0;

    // ── Session identity ──────────────────────────────────────────────────────

    private DateTimeOffset _sessionStart = DateTimeOffset.Now;
    private string         _sessionId    = NewSessionId();

    // ── Published state ────────────────────────────────────────────────────────

    private volatile WorkloadClassification? _currentClassification;
    private volatile MachineIdentityProfile? _machineIdentity;
    private IReadOnlyList<BehavioralPattern> _patterns  = [];
    private volatile UsageFingerprint?       _fingerprint;

    // ── Lifecycle flags ───────────────────────────────────────────────────────

    private bool _started;
    private bool _disposed;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Most recent workload classification, or null before first tick.</summary>
    public WorkloadClassification? CurrentClassification => _currentClassification;

    /// <summary>
    /// Long-term machine identity profile.
    /// Falls back to a freshly-built (possibly empty) profile if none recorded yet.
    /// </summary>
    public MachineIdentityProfile MachineIdentity =>
        _machineIdentity ?? _model.BuildProfile();

    /// <summary>Recurring behavioral patterns detected across sessions.</summary>
    public IReadOnlyList<BehavioralPattern> DetectedPatterns => _patterns;

    /// <summary>Recent completed sessions, newest first.</summary>
    public IReadOnlyList<SessionSummary> RecentSessions => _model.GetRecentSessions(20);

    /// <summary>Most recent point-in-time usage fingerprint.</summary>
    public UsageFingerprint? CurrentFingerprint => _fingerprint;

    /// <summary>
    /// Fired when a stable workload transition is committed.
    /// Fires on the telemetry thread — subscribers must dispatch to UI if needed.
    /// </summary>
    public event EventHandler<WorkloadClassification>? WorkloadChanged;

    // ── Construction ──────────────────────────────────────────────────────────

    public WorkloadProfilingService(
        ITelemetryService      telemetry,
        ISessionContextService? session = null)
    {
        _telemetry = telemetry;
        _session   = session;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Start()
    {
        if (_started || _disposed) return;
        _started      = true;
        _sessionStart = DateTimeOffset.Now;
        _sessionId    = NewSessionId();
        _telemetry.ReadingAvailable += OnReading;
    }

    public void Stop()
    {
        if (!_started) return;
        _telemetry.ReadingAvailable -= OnReading;
        FlushCurrentSession();
        _started = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    // ── Telemetry handler ─────────────────────────────────────────────────────

    private void OnReading(object? sender, TelemetrySnapshot snap)
    {
        if (_disposed) return;
        try
        {
            UpdateSessionPeaks(snap);

            // Rate-limit: only classify every 30 seconds
            var now = DateTimeOffset.Now;
            if ((now - _lastClassifiedAt).TotalSeconds < ClassificationIntervalSeconds)
                return;

            _lastClassifiedAt = now;
            Classify(snap);
        }
        catch
        {
            // Behavioral profiling must never propagate exceptions
        }
    }

    // ── Peak tracking ─────────────────────────────────────────────────────────

    private void UpdateSessionPeaks(TelemetrySnapshot snap)
    {
        if (snap.CpuPercent > _peakCpu) _peakCpu = snap.CpuPercent;
        if (snap.RamPercent > _peakRam) _peakRam = snap.RamPercent;

        if (snap.CpuTemperatureAvailable && snap.CpuTemperatureCelsius > _peakThermal)
            _peakThermal = snap.CpuTemperatureCelsius;

        if (snap.CpuTemperatureAvailable && snap.CpuTemperatureCelsius >= ThermalStressThresholdC)
            _hadThermal = true;

        if (snap.RamPercent >= MemoryPressureThresholdPct)
            _hadMemory = true;

        // Fingerprint is cheap to produce on every tick
        _fingerprint = SessionClassifier.CaptureFingerprint(snap);
    }

    // ── Classification ─────────────────────────────────────────────────────────

    private void Classify(TelemetrySnapshot snap)
    {
        var sessionCtx     = _session?.Current;
        var classification = SessionClassifier.Classify(snap, sessionCtx);

        _currentClassification = classification;

        // Check for a stable transition
        var transition = _transitions.Observe(classification);
        if (transition is not null)
        {
            _transitionCt++;
            try { WorkloadChanged?.Invoke(this, classification); }
            catch { /* event handler failures must not propagate */ }
        }
    }

    // ── Session flush ─────────────────────────────────────────────────────────

    /// <summary>
    /// Finalizes the current session, records it in the behavior model,
    /// and rebuilds the identity profile and pattern list.
    /// Called on Stop() or explicit Dispose().
    /// </summary>
    private void FlushCurrentSession()
    {
        try
        {
            var dominant = _transitions.CommittedWorkload;
            if (dominant == WorkloadType.Unknown) return;

            var duration = DateTimeOffset.Now - _sessionStart;
            if (duration < MinSessionDuration) return;

            var summary = new SessionSummary(
                SessionId:        _sessionId,
                DominantWorkload: dominant,
                StartedAt:        _sessionStart,
                EndedAt:          DateTimeOffset.Now,
                PeakCpuPct:       _peakCpu,
                PeakRamPct:       _peakRam,
                PeakThermalC:     _peakThermal,
                HadThermalStress: _hadThermal,
                HadMemoryPressure: _hadMemory,
                TransitionCount:  _transitionCt);

            _model.RecordSession(summary);

            // Rebuild derived views
            _machineIdentity = _model.BuildProfile();
            _patterns = WorkloadPatternDetector.DetectPatterns(_model.GetAllSessions());

            ResetSession();
        }
        catch { /* never crash during flush */ }
    }

    private void ResetSession()
    {
        _peakCpu      = 0;
        _peakRam      = 0;
        _peakThermal  = 0;
        _hadThermal   = false;
        _hadMemory    = false;
        _transitionCt = 0;
        _sessionId    = NewSessionId();
        _sessionStart = DateTimeOffset.Now;
    }

    private static string NewSessionId() => Guid.NewGuid().ToString("N")[..8];
}
