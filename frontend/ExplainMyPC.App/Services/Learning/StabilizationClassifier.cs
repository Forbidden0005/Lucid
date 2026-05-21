using ExplainMyPC.Services.Replay;

namespace ExplainMyPC.Services.Learning;

/// <summary>
/// Pure classification logic: takes a <see cref="RemediationComparison"/> and
/// produces an <see cref="OutcomeClassification"/> with a confidence score.
///
/// Classification thresholds (conservative to avoid over-claiming):
///
///   Improved          — Delta.ShowsImprovement=true AND
///                       (≥1 insight resolved OR ≥10% reduction in CPU/RAM/Disk)
///
///   PartiallyImproved — ShowsImprovement=true AND some positive signal
///                       but below the Improved threshold
///
///   Unchanged         — Direction is Stable with no insight changes
///
///   Worsened          — Direction is Worsening (metrics increased ≥5%)
///
///   InsufficientData  — comparison is null (no telemetry available)
///
/// This class is stateless — all methods are static.
/// </summary>
internal static class StabilizationClassifier
{
    // ── Thresholds ─────────────────────────────────────────────────────────────

    /// <summary>Minimum absolute metric reduction (%) to count as "significant improvement".</summary>
    private const double SignificantImprovementThreshold = 10.0;

    /// <summary>Minimum absolute metric increase (%) to classify as "worsened".</summary>
    private const double SignificantWorseningThreshold = 5.0;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Classifies the outcome of a remediation action based on before/after comparison.
    /// Returns <see cref="OutcomeClassification.InsufficientData"/> when comparison is null.
    /// </summary>
    public static (OutcomeClassification Outcome, int ConfidencePercent)
        Classify(RemediationComparison? comparison)
    {
        if (comparison is null)
            return (OutcomeClassification.InsufficientData, 0);

        var delta      = comparison.Delta;
        bool hasImproved = comparison.ShowsImprovement;

        // Check for decisive worsening first — most important to surface
        if (delta.Direction == DeltaDirection.Worsening && IsSignificantWorsening(delta))
            return (OutcomeClassification.Worsened, ConfidenceFrom(comparison, 75));

        if (!hasImproved)
        {
            // No improvement signal at all
            return (OutcomeClassification.Unchanged, ConfidenceFrom(comparison, 70));
        }

        // Has some improvement signal — check if it's decisive
        bool insightResolved    = delta.ResolvedInsights.Count > 0;
        bool significantMetric  = IsSignificantMetricImprovement(delta);

        if (insightResolved || significantMetric)
        {
            // Strong evidence: resolved insight OR major metric reduction
            int confidence = insightResolved && significantMetric ? 90
                           : insightResolved ? 80
                           : 72;
            return (OutcomeClassification.Improved, ConfidenceFrom(comparison, confidence));
        }

        // Some improvement but below the significant threshold
        return (OutcomeClassification.PartiallyImproved, ConfidenceFrom(comparison, 60));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when any primary metric (CPU, RAM, Disk) decreased by
    /// at least <see cref="SignificantImprovementThreshold"/> percentage points.
    /// Improvement = negative delta (metric went down).
    /// </summary>
    private static bool IsSignificantMetricImprovement(OperationalDelta delta)
    {
        // CpuChange/RamChange/DiskChange: positive = increased (worsened)
        // So improvement = negative value exceeding the threshold magnitude
        return -delta.CpuChange  >= SignificantImprovementThreshold ||
               -delta.RamChange  >= SignificantImprovementThreshold ||
               -delta.DiskChange >= SignificantImprovementThreshold;
    }

    /// <summary>
    /// Returns true when any primary metric increased by at least
    /// <see cref="SignificantWorseningThreshold"/> percentage points.
    /// </summary>
    private static bool IsSignificantWorsening(OperationalDelta delta)
    {
        return delta.CpuChange  >= SignificantWorseningThreshold ||
               delta.RamChange  >= SignificantWorseningThreshold ||
               delta.DiskChange >= SignificantWorseningThreshold;
    }

    /// <summary>
    /// Adjusts the base confidence score downward when telemetry coverage is sparse.
    /// Coverage is derived from the number of data points available in the snapshot windows.
    /// </summary>
    private static int ConfidenceFrom(RemediationComparison comparison, int baseConfidence)
    {
        // Reduce confidence when data is sparse
        int samplesBefore = comparison.SnapshotBefore.DataPointsAvailable;
        int samplesAfter  = comparison.SnapshotAfter.DataPointsAvailable;
        int minSamples    = Math.Min(samplesBefore, samplesAfter);

        return minSamples switch
        {
            >= 10 => baseConfidence,               // well-sampled — full confidence
            >= 5  => (int)(baseConfidence * 0.85), // moderate coverage
            >= 2  => (int)(baseConfidence * 0.70), // sparse
            _     => (int)(baseConfidence * 0.50), // very sparse — discount heavily
        };
    }
}
