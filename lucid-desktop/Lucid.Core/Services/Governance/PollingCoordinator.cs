namespace Lucid.Services.Governance;

/// <summary>
/// Bridges the runtime governance layer and the telemetry polling loop.
///
/// When <see cref="RuntimeGovernanceService"/> detects a mode change it calls
/// <see cref="ApplyMode"/> here. The coordinator translates the new mode into
/// concrete timing parameters from <see cref="AdaptiveSchedulingPolicy"/> and
/// pushes them to any registered <see cref="IAdaptiveTelemetryTarget"/>.
///
/// Responsibilities:
///   1. Translate <see cref="RuntimeMode"/> → polling intervals via policy table.
///   2. Notify all registered telemetry targets of the new poll interval.
///   3. Track the current mode and intervals for display in the governance page.
///
/// Thread safety: all public methods are safe to call from any thread.
/// </summary>
public sealed class PollingCoordinator
{
    // ── State ─────────────────────────────────────────────────────────────────

    private readonly object                          _lock    = new();
    private readonly List<IAdaptiveTelemetryTarget>  _targets = [];

    private RuntimeMode _currentMode              = RuntimeMode.Normal;
    private TimeSpan    _currentTelemetryInterval = AdaptiveSchedulingPolicy.GetTelemetryInterval(RuntimeMode.Normal);
    private TimeSpan    _currentProcessInterval   = AdaptiveSchedulingPolicy.GetProcessSampleInterval(RuntimeMode.Normal);

    // ── Registration ──────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a telemetry target that will receive interval updates when the
    /// runtime mode changes. Safe to call before or after <see cref="ApplyMode"/>.
    /// </summary>
    public void RegisterTarget(IAdaptiveTelemetryTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_lock)
        {
            if (!_targets.Contains(target))
                _targets.Add(target);
        }

        // Immediately push the current mode so late-registered targets start
        // with the correct interval instead of their compiled-in default.
        target.SetTelemetryInterval(_currentTelemetryInterval);
        target.SetProcessSampleInterval(_currentProcessInterval);
    }

    // ── Mode application ──────────────────────────────────────────────────────

    /// <summary>
    /// Applies the new runtime mode — updates intervals and notifies targets.
    /// Called by <see cref="RuntimeGovernanceService"/> on every mode transition
    /// (even Normal→Normal is safe; targets only receive a notification if the
    /// effective interval actually changed).
    /// </summary>
    public void ApplyMode(RuntimeMode newMode)
    {
        var newTelInterval  = AdaptiveSchedulingPolicy.GetTelemetryInterval(newMode);
        var newProcInterval = AdaptiveSchedulingPolicy.GetProcessSampleInterval(newMode);

        List<IAdaptiveTelemetryTarget> targets;
        bool telChanged, procChanged;

        lock (_lock)
        {
            telChanged  = newTelInterval  != _currentTelemetryInterval;
            procChanged = newProcInterval != _currentProcessInterval;

            _currentMode              = newMode;
            _currentTelemetryInterval = newTelInterval;
            _currentProcessInterval   = newProcInterval;

            targets = [.. _targets];
        }

        if (!telChanged && !procChanged)
            return;

        foreach (var t in targets)
        {
            try
            {
                if (telChanged)  t.SetTelemetryInterval(newTelInterval);
                if (procChanged) t.SetProcessSampleInterval(newProcInterval);
            }
            catch { /* best-effort — don't let one broken target block others */ }
        }
    }

    // ── Observation ───────────────────────────────────────────────────────────

    /// <summary>Current runtime mode held by the coordinator.</summary>
    public RuntimeMode CurrentMode
    {
        get { lock (_lock) return _currentMode; }
    }

    /// <summary>Currently active telemetry polling interval.</summary>
    public TimeSpan CurrentTelemetryInterval
    {
        get { lock (_lock) return _currentTelemetryInterval; }
    }

    /// <summary>Currently active process sample interval.</summary>
    public TimeSpan CurrentProcessInterval
    {
        get { lock (_lock) return _currentProcessInterval; }
    }
}
