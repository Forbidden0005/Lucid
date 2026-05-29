using Lucid.Services.Reasoning.Memory;

namespace Lucid.Services.Intelligence.Calibration;

/// <summary>
/// Coordinates adaptive confidence calibration for the cognitive reasoning pipeline.
///
/// The calibration engine:
///   1. Records calibration data when inference outcomes are observed
///   2. Periodically analyzes accuracy summaries for systematic drift
///   3. Produces a <see cref="CalibrationState"/> describing current calibration health
///   4. Exposes correction factors that the reasoning layer can optionally apply
///
/// Integration points:
///   - <see cref="HistoricalAccuracyTracker"/> — stores per-inference calibration records
///   - <see cref="ConfidenceDriftAnalyzer"/>   — detects systematic bias
///   - <see cref="RecommendationOutcomeTracker"/> — source of outcome confirmation signals
///
/// Transparency guarantee: all corrections are derived from visible data.
/// The correction factor is exposed via the diagnostics page and never silently applied.
///
/// Thread-safe: delegates to thread-safe components.
/// </summary>
public sealed class ConfidenceCalibrationEngine
{
    private readonly HistoricalAccuracyTracker _accuracyTracker;
    private volatile CalibrationState          _lastState = CalibrationState.Default;

    public ConfidenceCalibrationEngine(HistoricalAccuracyTracker? accuracyTracker = null)
    {
        _accuracyTracker = accuracyTracker ?? new HistoricalAccuracyTracker();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Records a calibration data point when an inference outcome is observed.
    /// Call this after the condition associated with an inference resolves.
    /// </summary>
    public void RecordOutcome(
        string   inferenceType,
        double   predictedConfidence,
        bool     wasConfirmed,
        DateTime predictedAt)
    {
        _accuracyTracker.Record(new CalibrationRecord
        {
            InferenceType       = inferenceType,
            PredictedConfidence = predictedConfidence,
            WasConfirmed        = wasConfirmed,
            PredictedAt         = predictedAt,
        });
    }

    /// <summary>
    /// Refreshes the calibration state from the current accuracy history.
    /// Call periodically (e.g., after each reasoning cycle or on demand).
    /// </summary>
    public CalibrationState Refresh()
    {
        var summaries  = _accuracyTracker.GetAllSummaries();
        var driftReport = ConfidenceDriftAnalyzer.Analyze(summaries);

        var state = new CalibrationState
        {
            LastRefreshedAt      = DateTime.UtcNow,
            TrackedTypeCount     = _accuracyTracker.TrackedTypeCount,
            DriftReport          = driftReport,
            AccuracySummaries    = summaries,
            RecommendedAdjustment = driftReport.RecommendedAdjustment,
        };

        _lastState = state;
        return state;
    }

    /// <summary>The most recent calibration state. Default state before first refresh.</summary>
    public CalibrationState CurrentState => _lastState;

    /// <summary>
    /// Returns the recommended confidence adjustment factor [0.7–1.3] for a specific
    /// inference type. Returns 1.0 (no adjustment) when calibration is well-formed
    /// or data is insufficient.
    /// </summary>
    public double GetAdjustmentFactor(string inferenceType)
    {
        var summary = _accuracyTracker.GetSummary(inferenceType);
        if (summary is null || !summary.IsReliable) return 1.0;

        // Per-type correction capped at ±20%.
        var correction = Math.Clamp(-summary.MeanCalibrationError * 0.5, -0.20, 0.20);
        return Math.Clamp(1.0 + correction, 0.70, 1.30);
    }
}

/// <summary>
/// Snapshot of the current confidence calibration health.
/// Exposed via <c>AppServices.CalibrationEngine.CurrentState</c>.
/// </summary>
public sealed class CalibrationState
{
    public DateTime LastRefreshedAt { get; init; }
    public int TrackedTypeCount { get; init; }
    public DriftReport DriftReport { get; init; } = DriftReport.NoDrift("No data yet.");
    public IReadOnlyList<AccuracySummary> AccuracySummaries { get; init; } = [];
    public double RecommendedAdjustment { get; init; } = 1.0;

    public static CalibrationState Default => new()
    {
        LastRefreshedAt   = DateTime.MinValue,
        TrackedTypeCount  = 0,
        RecommendedAdjustment = 1.0,
    };
}
