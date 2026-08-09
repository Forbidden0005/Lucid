namespace Lucid.Services.Intelligence.Calibration;

/// <summary>
/// Detects systematic confidence bias (drift) across inference types.
///
/// Drift analysis identifies patterns such as:
///   - "The ThermalStress rule is consistently over-confident by ~25%"
///   - "Overall confidence has been rising while confirmation rates decline"
///   - "StartupCongestion confidence is well-calibrated; CombinedPressure is not"
///
/// Results are used by <see cref="ConfidenceCalibrationEngine"/> to apply
/// correction factors when computing new confidence scores.
///
/// Transparency guarantee: every drift detection is based on recorded
/// <see cref="CalibrationRecord"/> data — not heuristics.
///
/// Thread-safe: stateless and pure — reads from immutable accuracy summaries.
/// </summary>
public static class ConfidenceDriftAnalyzer
{
    /// <summary>Minimum error magnitude to classify as meaningful drift.</summary>
    private const double DriftThreshold = 0.12;

    /// <summary>
    /// Analyzes accuracy summaries and returns a <see cref="DriftReport"/>
    /// describing any systematic confidence bias detected.
    /// </summary>
    public static DriftReport Analyze(IReadOnlyList<AccuracySummary> summaries)
    {
        if (summaries.Count == 0)
            return DriftReport.NoDrift("No calibration data available.");

        var reliable = summaries.Where(s => s.IsReliable).ToList();
        if (reliable.Count == 0)
            return DriftReport.NoDrift("Insufficient calibration data — still collecting.");

        // Compute overall mean calibration error.
        double overallMeanError = reliable.Average(s => s.MeanCalibrationError);

        // Identify the most drifted inference type.
        var mostDrifted = reliable
            .OrderByDescending(s => Math.Abs(s.MeanCalibrationError))
            .First();

        // Is the drift systemic (all types in same direction) or localized?
        bool isSystemic = reliable.All(s => s.MeanCalibrationError > DriftThreshold)  ||
                          reliable.All(s => s.MeanCalibrationError < -DriftThreshold);

        var direction = overallMeanError switch
        {
            > DriftThreshold  => DriftDirection.OverConfident,
            < -DriftThreshold => DriftDirection.UnderConfident,
            _                  => DriftDirection.Calibrated,
        };

        bool hasMeaningfulDrift = Math.Abs(overallMeanError) > DriftThreshold ||
                                  Math.Abs(mostDrifted.MeanCalibrationError) > DriftThreshold * 2;

        return new DriftReport(
            HasMeaningfulDrift:   hasMeaningfulDrift,
            Direction:            direction,
            OverallMeanError:     overallMeanError,
            IsSystemicDrift:      isSystemic,
            MostDriftedType:      hasMeaningfulDrift ? mostDrifted.InferenceType : null,
            MostDriftedError:     mostDrifted.MeanCalibrationError,
            RecommendedAdjustment: ComputeRecommendedAdjustment(overallMeanError),
            Explanation:           BuildExplanation(direction, overallMeanError, isSystemic,
                                                    mostDrifted));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the recommended confidence adjustment factor to apply to
    /// correct for the detected drift.
    /// Returns 1.0 (no adjustment) when drift is within acceptable bounds.
    /// </summary>
    private static double ComputeRecommendedAdjustment(double meanError)
    {
        if (Math.Abs(meanError) <= DriftThreshold) return 1.0;

        // Correction: scale down by the over-confidence error, capped at 20%.
        var correction = Math.Clamp(-meanError * 0.5, -0.20, 0.20);
        return Math.Clamp(1.0 + correction, 0.70, 1.30);
    }

    private static string BuildExplanation(
        DriftDirection direction,
        double         overallError,
        bool           isSystemic,
        AccuracySummary worstType)
    {
        var scope = isSystemic ? "across all inference types" : $"most prominently in '{worstType.InferenceType}'";

        return direction switch
        {
            DriftDirection.OverConfident =>
                $"Confidence values are running {overallError:P0} too high on average, {scope}. " +
                "Lucid is producing predictions with higher certainty than outcome data supports.",

            DriftDirection.UnderConfident =>
                $"Confidence values are running {Math.Abs(overallError):P0} too low on average, {scope}. " +
                "Lucid may be suppressing valid findings due to over-cautious confidence estimates.",

            _ =>
                "Confidence values are well-calibrated — predictions match observed outcomes.",
        };
    }
}

/// <summary>Report produced by <see cref="ConfidenceDriftAnalyzer"/>.</summary>
public sealed record DriftReport(
    bool           HasMeaningfulDrift,
    DriftDirection Direction,
    double         OverallMeanError,
    bool           IsSystemicDrift,
    string?        MostDriftedType,
    double         MostDriftedError,
    double         RecommendedAdjustment,
    string         Explanation)
{
    /// <summary>Creates a "no meaningful drift" report with an explanation.</summary>
    public static DriftReport NoDrift(string explanation) =>
        new(false, DriftDirection.Calibrated, 0, false, null, 0, 1.0, explanation);
}

/// <summary>The direction of confidence drift.</summary>
public enum DriftDirection
{
    /// <summary>Confidence is well-calibrated — predictions match outcomes.</summary>
    Calibrated,

    /// <summary>Engine is systematically more confident than outcomes justify.</summary>
    OverConfident,

    /// <summary>Engine is systematically less confident than outcomes justify.</summary>
    UnderConfident,
}
