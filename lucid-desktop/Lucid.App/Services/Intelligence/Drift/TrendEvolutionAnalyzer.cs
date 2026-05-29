namespace Lucid.Services.Intelligence.Drift;

/// <summary>
/// Analyzes how a metric's trend is changing over time (second-order analysis).
///
/// While first-order drift detection asks "is this metric higher than before?",
/// TrendEvolutionAnalyzer asks "is the rate of change itself increasing?"
///
/// This distinction matters for operational guidance:
///   - A metric that is 5% above baseline but stable → maintenance suggestion
///   - A metric that is 5% above baseline and accelerating → urgent investigation
///
/// Also classifies whether observed drift is likely:
///   - Intentional change (correlated with workload shift)
///   - Temporary anomaly (single spike, no persistence)
///   - Actual degradation (persistent, accelerating, no workload change)
///
/// Thread-safe: stateless and pure.
/// </summary>
public static class TrendEvolutionAnalyzer
{
    private const double VelocitySignificanceThreshold     = 0.10; // change/minute
    private const double AccelerationSignificanceThreshold = 0.05;

    /// <summary>
    /// Computes a <see cref="StabilityTrendModel"/> from a window of (time, value) samples.
    ///
    /// Requires at least 3 samples for velocity computation, 5 for acceleration.
    /// Returns a stable/zero model when insufficient data exists.
    /// </summary>
    public static StabilityTrendModel ComputeTrend(
        string                          metricName,
        IReadOnlyList<(DateTime At, double Value)> samples,
        TimeSpan?                       shortWindow = null,
        TimeSpan?                       longWindow  = null)
    {
        var sw = shortWindow ?? TimeSpan.FromMinutes(30);
        var lw = longWindow  ?? TimeSpan.FromHours(24);

        if (samples.Count < 3)
        {
            var current = samples.Count > 0 ? samples[^1].Value : 0;
            return new StabilityTrendModel
            {
                MetricName           = metricName,
                CurrentValue         = current,
                VelocityPerMinute    = 0,
                AccelerationPerMinute = 0,
                ShortWindow          = sw,
                LongWindow           = lw,
                LongWindowMean       = current,
            };
        }

        var ordered = samples.OrderBy(s => s.At).ToList();
        var latest  = ordered[^1];

        // Long-window mean for baseline comparison.
        var longCutoff   = latest.At - lw;
        var longSamples  = ordered.Where(s => s.At >= longCutoff).ToList();
        double longMean  = longSamples.Count > 0 ? longSamples.Average(s => s.Value) : latest.Value;

        // Short-window for velocity.
        var shortCutoff  = latest.At - sw;
        var shortSamples = ordered.Where(s => s.At >= shortCutoff).ToList();

        double velocity     = ComputeVelocity(shortSamples);
        double acceleration = ComputeAcceleration(shortSamples);

        return new StabilityTrendModel
        {
            MetricName            = metricName,
            CurrentValue          = latest.Value,
            VelocityPerMinute     = velocity,
            AccelerationPerMinute = acceleration,
            ShortWindow           = sw,
            LongWindow            = lw,
            LongWindowMean        = longMean,
        };
    }

    /// <summary>
    /// Classifies whether a trend reflects intentional change, a temporary anomaly,
    /// or actual degradation.
    /// </summary>
    public static TrendClassification ClassifyTrend(
        StabilityTrendModel trend,
        bool                workloadChangedRecently,
        TimeSpan?           driftPersistenceWindow = null)
    {
        var persistWindow = driftPersistenceWindow ?? TimeSpan.FromHours(2);

        // Intentional: workload recently changed and metrics shifted with it.
        if (workloadChangedRecently && !trend.IsAccelerating)
            return TrendClassification.IntentionalChange;

        // Temporary anomaly: one spike but velocity is not sustained.
        if (trend.DeviationFromNorm > 0.20 && trend.IsStable)
            return TrendClassification.TemporaryAnomaly;

        // Degradation: sustained, possibly accelerating rise with no workload change.
        if (trend.IsRising && !workloadChangedRecently)
            return trend.IsAccelerating
                ? TrendClassification.AcceleratingDegradation
                : TrendClassification.GradualDegradation;

        return TrendClassification.Stable;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static double ComputeVelocity(List<(DateTime At, double Value)> samples)
    {
        if (samples.Count < 2) return 0;

        // Simple linear regression slope as velocity.
        var first = samples.First();
        var last  = samples.Last();
        var dt    = (last.At - first.At).TotalMinutes;

        if (dt < 0.01) return 0;
        return (last.Value - first.Value) / dt;
    }

    private static double ComputeAcceleration(List<(DateTime At, double Value)> samples)
    {
        if (samples.Count < 5) return 0;

        // Approximate: compare velocity of first half vs velocity of second half.
        var mid    = samples.Count / 2;
        var first  = samples.Take(mid + 1).ToList();
        var second = samples.Skip(mid).ToList();

        double v1 = ComputeVelocity(first);
        double v2 = ComputeVelocity(second);

        var halfSpanMinutes = (samples[mid].At - samples[0].At).TotalMinutes;
        return halfSpanMinutes > 0 ? (v2 - v1) / halfSpanMinutes : 0;
    }
}

/// <summary>Classification of a detected trend's underlying cause.</summary>
public enum TrendClassification
{
    /// <summary>No significant trend — metric is stable.</summary>
    Stable,

    /// <summary>Trend is explained by a recent workload change.</summary>
    IntentionalChange,

    /// <summary>Single spike or elevation, not persisting — likely transient.</summary>
    TemporaryAnomaly,

    /// <summary>Metric is drifting upward without workload change — possible degradation.</summary>
    GradualDegradation,

    /// <summary>Drift is accelerating — increasing urgency.</summary>
    AcceleratingDegradation,
}
