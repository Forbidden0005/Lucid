namespace Lucid.Services.Intelligence.Baselines;

/// <summary>
/// Stateless utility for analyzing variance in a fixed window of values.
///
/// Unlike <c>WelfordStats</c> (online, unbounded streaming statistics),
/// VarianceAnalyzer operates on a finite, already-collected window of values
/// and returns a complete <see cref="VarianceStats"/> snapshot in one pass.
///
/// Use cases:
///   - Analyzing a 30-minute window of telemetry to compute stability
///   - Comparing variance in two baseline periods to detect drift
///   - Scoring pattern stability for PatternIntelligenceEngine
///
/// Thread-safe: stateless and pure.
/// </summary>
public static class VarianceAnalyzer
{
    /// <summary>
    /// Computes variance statistics for a window of values.
    /// Returns a zero-filled result for empty or single-element windows.
    /// </summary>
    public static VarianceStats Analyze(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return new VarianceStats(0, 0, 0, 0, 0, 0, IsReliable: false);

        if (values.Count == 1)
            return new VarianceStats(1, values[0], values[0], 0, 0, values[0], IsReliable: false);

        double sum  = 0;
        double min  = double.MaxValue;
        double max  = double.MinValue;

        foreach (var v in values)
        {
            sum += v;
            if (v < min) min = v;
            if (v > max) max = v;
        }

        double mean = sum / values.Count;

        double sumSqDiff = 0;
        foreach (var v in values)
            sumSqDiff += (v - mean) * (v - mean);

        double variance = sumSqDiff / (values.Count - 1); // sample variance
        double stdDev   = Math.Sqrt(variance);

        return new VarianceStats(
            SampleCount: values.Count,
            Mean:        mean,
            StdDev:      stdDev,
            Min:         min,
            Max:         max,
            Median:      ComputeMedian(values),
            IsReliable:  values.Count >= 10);
    }

    /// <summary>
    /// Returns true when the variance in <paramref name="a"/> and <paramref name="b"/>
    /// are similar enough to consider them from the same distribution.
    /// Threshold: |mean_a - mean_b| &lt; 2 * (pooled stddev).
    /// </summary>
    public static bool AreFromSameDistribution(VarianceStats a, VarianceStats b)
    {
        if (!a.IsReliable || !b.IsReliable) return false;
        var pooledStdDev = (a.StdDev + b.StdDev) / 2;
        var meanDiff     = Math.Abs(a.Mean - b.Mean);
        return pooledStdDev > 0 && meanDiff < 2 * pooledStdDev;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static double ComputeMedian(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        var mid    = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }
}

/// <summary>
/// Statistical summary for a finite window of observed values.
/// </summary>
public sealed record VarianceStats(
    int    SampleCount,
    double Mean,
    double StdDev,
    double Min,
    double Max,
    double Median,
    bool   IsReliable)
{
    /// <summary>Coefficient of variation (relative spread = StdDev / Mean).</summary>
    public double CoefficientOfVariation => Mean > 0 ? StdDev / Mean : 0;

    /// <summary>True when variance is high relative to the mean (CV > 0.25).</summary>
    public bool IsHighVariance => CoefficientOfVariation > 0.25;

    /// <summary>The range of values (Max - Min).</summary>
    public double Range => Max - Min;
}
