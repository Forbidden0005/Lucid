namespace Lucid.Services.Diagnostics;

/// <summary>
/// Controls software-level degraded-mode transitions for the diagnostics layer.
///
/// This is distinct from the hardware-driven <see cref="RuntimeMode"/> in the
/// governance layer. While <see cref="RuntimeMode"/> responds to CPU/GPU/thermal
/// pressure, <see cref="DegradedMode"/> responds to internal software failures:
/// sampler errors, executor crashes, service stalls, and persistence problems.
///
/// Transition thresholds:
///   Full → ReducedTelemetry:   Any sampler has 3+ consecutive failures.
///   ReducedTelemetry → Minimal: 2+ services are Degraded or Failed.
///   Minimal → RecoveryOnly:    3+ services are Failed.
///   Any → Full:                All services healthy, no sampler failures.
///
/// Recovery:
///   Downward transitions (toward Full) require all failure conditions to clear.
///   The controller does not apply hysteresis — callers should rate-limit
///   EvaluateAndTransition() (e.g. call it at most every 30 seconds).
///
/// Thread safety: all public methods are safe to call from any thread.
/// </summary>
public sealed class DegradedModeController
{
    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when the degraded mode transitions (on the calling thread).
    /// Subscribers should be cheap and non-blocking.
    /// </summary>
    public event EventHandler<DegradedModeChangedEventArgs>? ModeChanged;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly object _lock = new();
    private DegradedMode    _current = DegradedMode.Full;

    // ── Current mode ──────────────────────────────────────────────────────────

    public DegradedMode Current
    {
        get { lock (_lock) return _current; }
    }

    // ── Evaluation ────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates current failure state and transitions the mode if warranted.
    /// Call this after each significant failure or recovery event.
    /// </summary>
    public void EvaluateAndTransition(
        ServiceHealthMonitor healthMonitor,
        SamplerHealthTracker samplerTracker)
    {
        var (degradedCount, failedCount) = healthMonitor.GetDegradationCounts();
        bool anySamplerUnhealthy         = samplerTracker.AnyUnhealthy();

        DegradedMode target;

        if (failedCount >= 3)
            target = DegradedMode.RecoveryOnly;
        else if (failedCount >= 2 || degradedCount >= 2)
            target = DegradedMode.MinimalMonitoring;
        else if (anySamplerUnhealthy || degradedCount >= 1 || failedCount >= 1)
            target = DegradedMode.ReducedTelemetry;
        else
            target = DegradedMode.Full;

        DegradedMode previous;
        bool changed;

        lock (_lock)
        {
            previous = _current;
            changed  = target != _current;
            _current = target;
        }

        if (changed)
        {
            try
            {
                ModeChanged?.Invoke(this, new DegradedModeChangedEventArgs(previous, target));
            }
            catch { /* event subscriber failures must not propagate */ }
        }
    }

    /// <summary>
    /// Forcibly resets to Full mode. Used by <see cref="RecoveryCoordinator"/>
    /// after a successful recovery that resolves all known failures.
    /// </summary>
    public void ForceReset()
    {
        DegradedMode previous;
        lock (_lock)
        {
            previous = _current;
            _current = DegradedMode.Full;
        }

        if (previous != DegradedMode.Full)
        {
            try
            {
                ModeChanged?.Invoke(this,
                    new DegradedModeChangedEventArgs(previous, DegradedMode.Full));
            }
            catch { /* best-effort */ }
        }
    }
}

/// <summary>
/// Event args for <see cref="DegradedModeController.ModeChanged"/>.
/// </summary>
public sealed class DegradedModeChangedEventArgs(
    DegradedMode previousMode,
    DegradedMode newMode) : EventArgs
{
    public DegradedMode PreviousMode { get; } = previousMode;
    public DegradedMode NewMode      { get; } = newMode;

    /// <summary>True when the platform is entering a more restricted mode.</summary>
    public bool IsEscalation => NewMode > PreviousMode;

    /// <summary>True when the platform is recovering toward full functionality.</summary>
    public bool IsRecovery   => NewMode < PreviousMode;

    /// <summary>Human-readable description of the transition.</summary>
    public string Description => (PreviousMode, NewMode) switch
    {
        (DegradedMode.Full, DegradedMode.ReducedTelemetry) =>
            "Monitoring reduced — one or more telemetry samplers encountered errors.",
        (DegradedMode.ReducedTelemetry, DegradedMode.MinimalMonitoring) =>
            "Monitoring further reduced — multiple subsystems are experiencing issues.",
        (DegradedMode.MinimalMonitoring, DegradedMode.RecoveryOnly) =>
            "Platform in recovery-only mode — background analysis paused to stabilize.",
        (_, DegradedMode.Full) =>
            "All subsystems healthy — full monitoring restored.",
        var (from, to) when to > from =>
            $"Degraded mode escalated from {from} to {to}.",
        var (from, to) =>
            $"Degraded mode reduced from {from} to {to} — conditions improving.",
    };
}
