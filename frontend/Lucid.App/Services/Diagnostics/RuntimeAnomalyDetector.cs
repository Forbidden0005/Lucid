namespace Lucid.Services.Diagnostics;

/// <summary>
/// Detects runtime anomalies that cannot be inferred from individual service
/// events — specifically, gaps in the telemetry stream, memory growth, and
/// rapid governance-mode oscillation.
///
/// Anomaly categories:
///   PollingOverrun — gap between ReadingAvailable events exceeds 3× the
///                    expected poll interval (indicates the poll loop stalled).
///   MemoryPressure — process working set exceeds <see cref="MemoryWarningMb"/>
///                    (Lucid is growing unexpectedly large).
///   ModeOscillation — governance mode changed more than
///                     <see cref="OscillationThreshold"/> times in a short window
///                     (indicates a noisy threshold or unstable workload).
///
/// Usage:
///   AppServices calls <see cref="OnTelemetryReceived"/> on each ReadingAvailable
///   event, <see cref="OnGovernanceModeChanged"/> on each ModeChanged event, and
///   periodically calls <see cref="CheckMemoryPressure"/> from the self-telemetry
///   timer.  Detected anomalies are returned to InternalDiagnosticsService for
///   forwarding to DiagnosticsEventAggregator.
///
/// Thread safety: all public methods are safe to call from any thread.
/// </summary>
public sealed class RuntimeAnomalyDetector
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>Default expected poll interval (Normal mode).</summary>
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1.5);

    /// <summary>Multiplier: gap exceeding this factor is flagged as overrun.</summary>
    private const double OverrunFactor = 3.0;

    /// <summary>Process working set above this value is flagged as memory pressure.</summary>
    private const double MemoryWarningMb = 250.0;

    /// <summary>
    /// More than this many governance-mode changes within the oscillation window
    /// is flagged as oscillation.
    /// </summary>
    private const int OscillationThreshold = 6;

    /// <summary>Window over which governance-mode changes are counted.</summary>
    private static readonly TimeSpan OscillationWindow = TimeSpan.FromMinutes(2);

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly object             _lock              = new();
    private          DateTimeOffset     _lastTelemetry     = DateTimeOffset.MinValue;
    private          TimeSpan           _expectedInterval  = DefaultPollInterval;
    private readonly Queue<DateTimeOffset> _modeChangeTimes = new();

    // ── Anomaly detection ──────────────────────────────────────────────────────

    /// <summary>
    /// Called on each ReadingAvailable event.  Returns a <see cref="DetectedAnomaly"/>
    /// when a polling overrun is detected, otherwise null.
    /// </summary>
    public DetectedAnomaly? OnTelemetryReceived(DateTimeOffset timestamp, TimeSpan currentInterval)
    {
        lock (_lock)
        {
            _expectedInterval = currentInterval;

            if (_lastTelemetry == DateTimeOffset.MinValue)
            {
                _lastTelemetry = timestamp;
                return null;
            }

            var gap       = timestamp - _lastTelemetry;
            _lastTelemetry = timestamp;

            double threshold = _expectedInterval.TotalMilliseconds * OverrunFactor;

            if (gap.TotalMilliseconds > threshold)
            {
                return new DetectedAnomaly(
                    AnomalyType: "PollingOverrun",
                    Message:     $"Telemetry polling stalled for {gap.TotalSeconds:F1}s " +
                                 $"(expected ≤{_expectedInterval.TotalSeconds:F1}s).",
                    Severity:    gap.TotalMilliseconds > threshold * 2
                                     ? DiagnosticSeverity.Warning
                                     : DiagnosticSeverity.Info);
            }

            return null;
        }
    }

    /// <summary>
    /// Called when the governance mode changes.
    /// Returns an oscillation anomaly if mode changes are happening too rapidly.
    /// </summary>
    public DetectedAnomaly? OnGovernanceModeChanged(DateTimeOffset timestamp)
    {
        lock (_lock)
        {
            _modeChangeTimes.Enqueue(timestamp);

            // Evict entries outside the oscillation window.
            while (_modeChangeTimes.Count > 0 &&
                   (timestamp - _modeChangeTimes.Peek()) > OscillationWindow)
            {
                _modeChangeTimes.Dequeue();
            }

            if (_modeChangeTimes.Count >= OscillationThreshold)
            {
                return new DetectedAnomaly(
                    AnomalyType: "ModeOscillation",
                    Message:     $"Governance mode changed {_modeChangeTimes.Count} times " +
                                 $"in {OscillationWindow.TotalMinutes:F0} minutes — " +
                                 "a threshold may be misconfigured.",
                    Severity:    DiagnosticSeverity.Warning);
            }

            return null;
        }
    }

    /// <summary>
    /// Checks current process memory usage.
    /// Returns an anomaly when working set exceeds the warning threshold.
    /// </summary>
    public DetectedAnomaly? CheckMemoryPressure()
    {
        try
        {
            double workingSetMb = Environment.WorkingSet / 1_048_576.0;

            if (workingSetMb > MemoryWarningMb)
            {
                return new DetectedAnomaly(
                    AnomalyType: "MemoryPressure",
                    Message:     $"Process working set is {workingSetMb:F0} MB — " +
                                 $"above the {MemoryWarningMb:F0} MB threshold.",
                    Severity:    workingSetMb > MemoryWarningMb * 1.5
                                     ? DiagnosticSeverity.Critical
                                     : DiagnosticSeverity.Warning);
            }
        }
        catch { /* WorkingSet query can fail in constrained environments */ }

        return null;
    }

    /// <summary>Updates the expected poll interval (called when governance mode changes).</summary>
    public void SetExpectedInterval(TimeSpan interval)
    {
        lock (_lock)
        {
            if (interval > TimeSpan.Zero)
                _expectedInterval = interval;
        }
    }
}

/// <summary>A runtime anomaly detected by <see cref="RuntimeAnomalyDetector"/>.</summary>
public sealed record DetectedAnomaly(
    string             AnomalyType,
    string             Message,
    DiagnosticSeverity Severity);
