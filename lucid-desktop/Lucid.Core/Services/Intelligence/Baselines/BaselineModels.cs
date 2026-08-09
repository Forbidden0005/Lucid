namespace Lucid.Services.Intelligence.Baselines;

/// <summary>
/// Baseline statistics for one operational metric within one workload context.
/// Immutable snapshot — updated by AdaptiveBaselineTracker each cycle.
/// </summary>
public sealed record WorkloadBaselineProfile
{
    /// <summary>Workload label this profile applies to (e.g. "Gaming", "Idle", "Mixed").</summary>
    public required string WorkloadLabel { get; init; }

    /// <summary>Metric name this profile tracks (e.g. "CpuPercent", "CpuTemperatureCelsius").</summary>
    public required string MetricName { get; init; }

    /// <summary>Number of observations incorporated into this profile.</summary>
    public required long SampleCount { get; init; }

    /// <summary>Running mean of the metric under this workload.</summary>
    public required double Mean { get; init; }

    /// <summary>Running standard deviation of the metric under this workload.</summary>
    public required double StdDev { get; init; }

    /// <summary>UTC time this profile was last updated.</summary>
    public required DateTime LastUpdatedAt { get; init; }

    // ── Computed ──────────────────────────────────────────────────────────────

    /// <summary>True when enough samples exist for the baseline to be statistically reliable.</summary>
    public bool IsReliable => SampleCount >= 50;

    /// <summary>Coefficient of variation — relative spread of values (StdDev / Mean).</summary>
    public double CoefficientOfVariation => Mean > 0 ? StdDev / Mean : 0;

    /// <summary>True when the metric shows high variability for this workload.</summary>
    public bool IsHighVariance => CoefficientOfVariation > 0.30;

    /// <summary>The "normal range" for this metric under this workload (Mean ± 2σ).</summary>
    public (double Low, double High) NormalRange =>
        (Math.Max(0, Mean - 2 * StdDev), Mean + 2 * StdDev);

    /// <summary>True when <paramref name="value"/> falls within the normal range.</summary>
    public bool IsNormal(double value)
    {
        var (low, high) = NormalRange;
        return value >= low && value <= high;
    }
}

/// <summary>
/// Result of a normality classification for a specific metric value.
/// </summary>
public sealed record OperationalNormResult
{
    /// <summary>The metric value that was classified.</summary>
    public required double Value { get; init; }

    /// <summary>The baseline mean this value was compared against.</summary>
    public required double BaselineMean { get; init; }

    /// <summary>Standard deviation of the baseline.</summary>
    public required double BaselineStdDev { get; init; }

    /// <summary>Z-score of the value relative to the baseline.</summary>
    public required double ZScore { get; init; }

    /// <summary>How the value compares to the baseline.</summary>
    public required NormClassification Classification { get; init; }

    /// <summary>True when the baseline had enough samples to be reliable.</summary>
    public required bool IsBaselineReliable { get; init; }

    /// <summary>Human-readable explanation of the classification.</summary>
    public string Explanation => Classification switch
    {
        NormClassification.Normal          => "Within expected range for this workload.",
        NormClassification.ElevatedModerate => "Slightly elevated relative to normal.",
        NormClassification.ElevatedHigh     => "Notably elevated — above typical range.",
        NormClassification.Suppressed       => "Below typical range.",
        NormClassification.UnknownBaseline  => "No baseline available for this workload yet.",
        _                                   => "Classification unavailable.",
    };
}

/// <summary>How a metric value compares to the operational baseline.</summary>
public enum NormClassification
{
    /// <summary>Value is within 2 standard deviations of the mean.</summary>
    Normal,

    /// <summary>Value is between 2 and 3 standard deviations above the mean.</summary>
    ElevatedModerate,

    /// <summary>Value is more than 3 standard deviations above the mean.</summary>
    ElevatedHigh,

    /// <summary>Value is more than 2 standard deviations below the mean.</summary>
    Suppressed,

    /// <summary>No reliable baseline exists for this workload context.</summary>
    UnknownBaseline,
}
