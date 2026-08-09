namespace Lucid.Services.Baseline;

/// <summary>
/// Numerically stable online accumulator for mean and variance, using
/// Welford's algorithm (1962).
///
/// Properties:
///   • O(1) per update — no buffer, no sort, no heap allocations.
///   • Numerically stable for all finite inputs (no catastrophic cancellation).
///   • Soft-decay at <see cref="MaxCount"/> — recent observations gradually
///     outweigh very old ones without discarding historical knowledge.
///
/// Welford's recurrences:
///   delta  = value − mean(n−1)
///   mean   += delta / n
///   M₂     += delta × (value − mean(n))
///   variance = M₂ / (n − 1)   [Bessel-corrected sample variance]
///
/// Soft-decay (forgetting):
///   When Count reaches <see cref="MaxCount"/> (~6 h at 1.5 s/tick), both
///   Count and M₂ are halved before the next update. Mean is preserved exactly.
///   The effect is a decay time constant of ≈ MaxCount/2 observations, making
///   the model gradually adapt to genuine behavioural changes (new apps
///   installed, new hardware, usage pattern shifts) without restarting cold.
///
/// Threading:
///   Not thread-safe. In Lucid all updates and reads occur on the UI
///   thread, which is where <see cref="ITelemetryService.ReadingAvailable"/>
///   fires (marshalled there by <see cref="WindowsTelemetryService"/>).
/// </summary>
public sealed class WelfordStats
{
    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>
    /// After this many observations, soft-decay activates.
    /// 14 400 samples ≈ 6 hours at the 1.5-second poll rate.
    /// </summary>
    public const long MaxCount = 14_400;

    /// <summary>
    /// Minimum observations before anomaly-detection methods are trusted.
    /// 200 samples ≈ 5 minutes of telemetry — enough for a stable mean and
    /// a meaningful standard deviation.
    /// </summary>
    public const int MinAnomalyCount = 200;

    /// <summary>
    /// Minimum standard deviation before Z-score calculation is considered
    /// meaningful. Below this threshold the distribution is too flat to
    /// distinguish signal from noise (common for very stable metrics).
    /// </summary>
    private const double MinStdDevForZScore = 0.5;

    // ── Accumulator state ─────────────────────────────────────────────────────

    /// <summary>Number of observations incorporated (may be reduced by soft-decay).</summary>
    public long   Count { get; private set; }

    /// <summary>Current running mean of all observed values.</summary>
    public double Mean  { get; private set; }

    /// <summary>
    /// Running sum of squared deviations from the mean (M₂ in Welford's notation).
    /// Variance = M₂ / (Count − 1).
    /// </summary>
    public double M2    { get; private set; }

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>Creates a fresh accumulator with no observations.</summary>
    public WelfordStats() { }

    /// <summary>
    /// Restores a previously persisted accumulator from its three state fields.
    /// Used by <see cref="SystemBaselineService"/> when loading baseline data
    /// from disk on startup.
    /// </summary>
    public WelfordStats(long count, double mean, double m2)
    {
        Count = count;
        Mean  = mean;
        M2    = m2;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Incorporates one new observation. Updates Count, Mean, and M₂ in O(1).
    ///
    /// When Count reaches <see cref="MaxCount"/>, a soft-decay step halves the
    /// effective weight of all historical observations before applying the
    /// new value, preventing the model from becoming permanently anchored to
    /// an old behavioural baseline.
    /// </summary>
    public void Update(double value)
    {
        // Soft-decay: preserve the mean but halve the effective sample count.
        // This makes the next ~MaxCount/2 observations count as much as all
        // prior history combined, providing a gentle relearning capability.
        if (Count >= MaxCount)
        {
            Count /= 2;
            M2    /= 2;
            // Mean is exact — no decay needed.
        }

        // Welford's online update.
        Count++;
        double delta  = value - Mean;
        Mean         += delta / Count;
        M2           += delta * (value - Mean);  // uses updated Mean (important)
    }

    // ── Derived statistics ────────────────────────────────────────────────────

    /// <summary>Sample variance (Bessel-corrected: divides by n−1).</summary>
    public double Variance => Count < 2 ? 0 : M2 / (Count - 1);

    /// <summary>Sample standard deviation.</summary>
    public double StdDev   => Math.Sqrt(Variance);

    /// <summary>
    /// True when the accumulator has collected enough observations for
    /// anomaly-detection results to be statistically meaningful.
    /// </summary>
    public bool HasSufficientData => Count >= MinAnomalyCount;

    /// <summary>
    /// Returns how many standard deviations above (positive) or below
    /// (negative) the learned mean the given value lies.
    ///
    /// Returns 0 when:
    ///   • StdDev is below <see cref="MinStdDevForZScore"/> (distribution too flat), or
    ///   • <see cref="HasSufficientData"/> is false (not enough data yet).
    /// </summary>
    public double ZScore(double value) =>
        HasSufficientData && StdDev >= MinStdDevForZScore
            ? (value - Mean) / StdDev
            : 0;

    // ── Persistence restore ───────────────────────────────────────────────────

    /// <summary>
    /// Overwrites this accumulator's state with persisted values.
    /// Used exclusively by <see cref="SystemBaselineService"/> when loading
    /// baseline.json on startup. The three values must come from a prior
    /// <see cref="WelfordStats"/> serialization to preserve statistical validity.
    /// </summary>
    internal void Restore(long count, double mean, double m2)
    {
        Count = count;
        Mean  = mean;
        M2    = m2;
    }

    // ── Diagnostics ───────────────────────────────────────────────────────────

    public override string ToString() =>
        $"μ={Mean:F1} σ={StdDev:F1} n={Count:N0}";
}
