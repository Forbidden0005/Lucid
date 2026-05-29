namespace Lucid.Services.Intelligence.Drift;

/// <summary>
/// Encapsulates the direction, velocity, and acceleration of an operational trend.
///
/// Unlike a simple "is this metric elevated?" check, the StabilityTrendModel
/// captures HOW FAST a metric is changing and WHETHER the rate of change itself
/// is accelerating. This enables Lucid to distinguish:
///   - "CPU is elevated but stable" (high, velocity ≈ 0)
///   - "CPU is rising slowly over hours" (high, velocity > 0, acceleration ≈ 0)
///   - "CPU is rising increasingly quickly" (high, velocity > 0, acceleration > 0)
///
/// Immutable — created by <see cref="TrendEvolutionAnalyzer"/> per analysis cycle.
/// </summary>
public sealed record StabilityTrendModel
{
    /// <summary>The metric this trend describes (e.g. "CpuPercent", "CpuTemperatureCelsius").</summary>
    public required string MetricName { get; init; }

    /// <summary>Current metric value at the time of analysis.</summary>
    public required double CurrentValue { get; init; }

    /// <summary>
    /// Rate of change per minute: positive = rising, negative = falling, ≈ 0 = stable.
    /// Computed over the short-window period.
    /// </summary>
    public required double VelocityPerMinute { get; init; }

    /// <summary>
    /// Rate of change of the velocity (second derivative).
    /// Positive = accelerating, negative = decelerating.
    /// </summary>
    public required double AccelerationPerMinute { get; init; }

    /// <summary>Short window duration used for velocity computation.</summary>
    public required TimeSpan ShortWindow { get; init; }

    /// <summary>Long window duration used for baseline comparison.</summary>
    public required TimeSpan LongWindow { get; init; }

    /// <summary>Mean over the long window (context for current value).</summary>
    public required double LongWindowMean { get; init; }

    /// <summary>UTC time this model was computed.</summary>
    public DateTime ComputedAt { get; init; } = DateTime.UtcNow;

    // ── Computed ──────────────────────────────────────────────────────────────

    /// <summary>True when the metric is rising meaningfully.</summary>
    public bool IsRising => VelocityPerMinute > 0.10;

    /// <summary>True when the metric is falling meaningfully.</summary>
    public bool IsFalling => VelocityPerMinute < -0.10;

    /// <summary>True when the rise is accelerating (worse over time).</summary>
    public bool IsAccelerating => IsRising && AccelerationPerMinute > 0.05;

    /// <summary>True when the velocity is near-zero (stable value).</summary>
    public bool IsStable => Math.Abs(VelocityPerMinute) <= 0.10;

    /// <summary>
    /// Deviation from the long-window mean as a percentage of the mean.
    /// Positive = currently elevated above historical norm.
    /// </summary>
    public double DeviationFromNorm =>
        LongWindowMean > 0 ? (CurrentValue - LongWindowMean) / LongWindowMean : 0;

    /// <summary>Human-readable trend description.</summary>
    public string TrendDescription =>
        IsStable      ? $"{MetricName} is stable near {CurrentValue:F1}"
        : IsRising    ? $"{MetricName} rising at {VelocityPerMinute:+#.##;-#.##}/min"
        : IsFalling   ? $"{MetricName} falling at {VelocityPerMinute:+#.##;-#.##}/min"
        : $"{MetricName} at {CurrentValue:F1}";
}
