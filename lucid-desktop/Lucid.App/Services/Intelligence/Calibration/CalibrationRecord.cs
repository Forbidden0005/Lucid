namespace Lucid.Services.Intelligence.Calibration;

/// <summary>
/// Immutable record linking a predicted confidence value to its observed outcome.
///
/// Confidence calibration works by comparing:
///   - What confidence level the engine predicted at inference time
///   - Whether the inferred condition actually manifested as expected
///
/// A well-calibrated engine that says "80% confident" should be right ~80% of the time.
/// <see cref="HistoricalAccuracyTracker"/> accumulates these records and
/// <see cref="ConfidenceDriftAnalyzer"/> detects systematic bias.
///
/// Privacy guarantee: no user-identifiable data. Operational types and scores only.
/// </summary>
public sealed record CalibrationRecord
{
    /// <summary>Unique identifier for this calibration record.</summary>
    public Guid RecordId { get; init; } = Guid.NewGuid();

    /// <summary>The inference type that was calibrated (e.g. "StartupCongestion").</summary>
    public required string InferenceType { get; init; }

    /// <summary>The confidence score predicted at inference time [0, 1].</summary>
    public required double PredictedConfidence { get; init; }

    /// <summary>Whether the inferred condition was confirmed by a subsequent observation.</summary>
    public required bool WasConfirmed { get; init; }

    /// <summary>UTC time the prediction was made.</summary>
    public required DateTime PredictedAt { get; init; }

    /// <summary>UTC time the outcome was observed.</summary>
    public DateTime ObservedAt { get; init; } = DateTime.UtcNow;

    // ── Computed ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Calibration error: positive = over-confident, negative = under-confident.
    /// Error = PredictedConfidence - (1.0 if confirmed, 0.0 if not).
    /// </summary>
    public double CalibrationError =>
        PredictedConfidence - (WasConfirmed ? 1.0 : 0.0);

    /// <summary>Absolute calibration error magnitude.</summary>
    public double AbsoluteError => Math.Abs(CalibrationError);

    /// <summary>Time from prediction to outcome observation.</summary>
    public TimeSpan ObservationDelay => ObservedAt - PredictedAt;
}
