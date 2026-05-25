using Lucid.Helpers;
using Lucid.Services.Baseline;

namespace Lucid.Services.Intelligence;

// ── Interface ───────────────────────────────────────────────────────────────────

/// <summary>
/// Detects short-term behavioral anomalies by comparing live telemetry to
/// this machine's learned <see cref="MachineBaseline"/> via z-score thresholds.
///
/// Anomalies represent momentary deviations — not long-term drift.
/// All findings use confidence-aware, non-alarming language.
/// </summary>
public interface IAnomalyDetectionService
{
    /// <summary>Currently active anomalies. Empty when telemetry is within baseline norms.</summary>
    IReadOnlyList<DetectedAnomaly> CurrentAnomalies { get; }

    /// <summary>
    /// Raised on the UI thread whenever the active anomaly set changes.
    /// Fires at telemetry cadence (~1.5 s) only when the set actually changes.
    /// </summary>
    event EventHandler<IReadOnlyList<DetectedAnomaly>>? AnomaliesUpdated;
}

// ── Implementation ──────────────────────────────────────────────────────────────

/// <summary>
/// Subscribes to <see cref="ITelemetryService.ReadingAvailable"/> and evaluates
/// each snapshot against the machine's Welford baseline using z-scores.
///
/// Design notes:
/// • Only fires <see cref="AnomaliesUpdated"/> when the anomaly ID set changes
///   — not on every tick — to avoid unnecessary ViewModel work.
/// • Requires a valid baseline (≥ 200 observations) before any anomaly fires.
/// • Uses both z-score and absolute delta floors to avoid flagging trivial noise.
/// • Events are raised on the UI thread (inherited from ITelemetryService).
/// </summary>
public sealed class AnomalyDetectionService : IAnomalyDetectionService
{
    private readonly ISystemBaselineService _baseline;

    private IReadOnlyList<DetectedAnomaly> _current = [];
    private HashSet<string>                _activeIds = [];

    /// <inheritdoc/>
    public IReadOnlyList<DetectedAnomaly> CurrentAnomalies => _current;

    /// <inheritdoc/>
    public event EventHandler<IReadOnlyList<DetectedAnomaly>>? AnomaliesUpdated;

    public AnomalyDetectionService(ISystemBaselineService baseline, ITelemetryService telemetry)
    {
        _baseline = baseline;
        telemetry.ReadingAvailable += OnReadingAvailable;

        // Seed immediately from the most recent reading.
        if (telemetry.LastReading is { } snap)
            Evaluate(snap);
    }

    // ── Event handler ─────────────────────────────────────────────────────────

    private void OnReadingAvailable(object? sender, TelemetrySnapshot snap) => Evaluate(snap);

    // ── Core evaluation ───────────────────────────────────────────────────────

    private void Evaluate(TelemetrySnapshot snap)
    {
        var raw = _baseline.CurrentBaseline;
        if (raw is null) return;  // Baseline still warming up — never fire prematurely.

        var now       = DateTime.UtcNow;
        var anomalies = new List<DetectedAnomaly>(3);

        // ── CPU anomaly ───────────────────────────────────────────────────────
        if (raw.IdleCpuReady && snap.CpuPercent - raw.IdleCpuMean >= MachineBaseline.CpuMinAbsoluteDelta)
        {
            var z = raw.IdleCpuZScore(snap.CpuPercent);
            if (z >= MachineBaseline.CpuSigmaRecommendation)
            {
                var severity = z >= MachineBaseline.CpuSigmaWarning
                    ? AnomalyIntelligenceSeverity.High
                    : z >= 2.5
                        ? AnomalyIntelligenceSeverity.Elevated
                        : AnomalyIntelligenceSeverity.Moderate;

                anomalies.Add(new DetectedAnomaly(
                    Metric:          "CPU",
                    ExpectedMean:    raw.IdleCpuMean,
                    ExpectedStdDev:  raw.IdleCpuStdDev,
                    ActualValue:     snap.CpuPercent,
                    DivergenceScore: z,
                    Severity:        severity,
                    Description:     BuildCpuDescription(snap.CpuPercent, raw.IdleCpuMean, z),
                    DetectedAt:      now));
            }
        }

        // ── RAM anomaly ───────────────────────────────────────────────────────
        if (raw.NormalRamReady && snap.RamPercent - raw.NormalRamMean >= MachineBaseline.RamMinAbsoluteDelta)
        {
            var z = raw.RamZScore(snap.RamPercent);
            if (z >= MachineBaseline.RamSigmaRecommendation)
            {
                var severity = z >= MachineBaseline.RamSigmaWarning
                    ? AnomalyIntelligenceSeverity.High
                    : z >= 3.0
                        ? AnomalyIntelligenceSeverity.Elevated
                        : AnomalyIntelligenceSeverity.Moderate;

                anomalies.Add(new DetectedAnomaly(
                    Metric:          "RAM",
                    ExpectedMean:    raw.NormalRamMean,
                    ExpectedStdDev:  raw.NormalRamStdDev,
                    ActualValue:     snap.RamPercent,
                    DivergenceScore: z,
                    Severity:        severity,
                    Description:     BuildRamDescription(snap.RamPercent, raw.NormalRamMean, z),
                    DetectedAt:      now));
            }
        }

        // ── Thermal anomaly ───────────────────────────────────────────────────
        if (raw.NormalThermalReady
            && snap.CpuTemperatureAvailable
            && snap.CpuTemperatureCelsius - raw.NormalThermalMean >= MachineBaseline.ThermalMinAbsoluteDelta)
        {
            var z = raw.ThermalZScore(snap.CpuTemperatureCelsius);
            if (z >= MachineBaseline.ThermalSigmaRecommendation)
            {
                var severity = z >= MachineBaseline.ThermalSigmaWarning
                    ? AnomalyIntelligenceSeverity.High
                    : z >= 2.5
                        ? AnomalyIntelligenceSeverity.Elevated
                        : AnomalyIntelligenceSeverity.Moderate;

                anomalies.Add(new DetectedAnomaly(
                    Metric:          "Thermal",
                    ExpectedMean:    raw.NormalThermalMean,
                    ExpectedStdDev:  raw.NormalThermalStdDev,
                    ActualValue:     snap.CpuTemperatureCelsius,
                    DivergenceScore: z,
                    Severity:        severity,
                    Description:     BuildThermalDescription(snap.CpuTemperatureCelsius, raw.NormalThermalMean, z),
                    DetectedAt:      now));
            }
        }

        // ── Publish only when the set changes ─────────────────────────────────
        var newIds = new HashSet<string>(anomalies.Select(a => $"{a.Metric}:{a.Severity}"));
        if (newIds.SetEquals(_activeIds)) return;

        _activeIds = newIds;
        _current   = anomalies;
        AnomaliesUpdated?.Invoke(this, _current);
    }

    // ── Description builders ──────────────────────────────────────────────────

    private static string BuildCpuDescription(double actual, double mean, double z) =>
        $"CPU usage ({actual:0}%) appears elevated compared to this machine's typical idle level " +
        $"of ~{mean:0}% ({z:F1}σ above baseline).";

    private static string BuildRamDescription(double actual, double mean, double z) =>
        $"RAM usage ({actual:0}%) appears higher than usual for this machine " +
        $"(typical ~{mean:0}%, {z:F1}σ above baseline).";

    private static string BuildThermalDescription(double actual, double mean, double z) =>
        $"CPU temperature ({actual:0}°C) is elevated relative to this machine's normal range " +
        $"of ~{mean:0}°C ({z:F1}σ above baseline).";
}
