using ExplainMyPC.Services.Telemetry;

namespace ExplainMyPC.Services.Intelligence;

/// <summary>
/// Computational helpers for trend detection over telemetry history.
///
/// All methods are pure — no state, no I/O — and are designed to be
/// called synchronously on the UI thread from <see cref="IInsightRule.Evaluate"/>.
///
/// Performance:
///   Every method is O(n) over the sample list. For a full 30-minute window
///   at 1.5 s per tick (≈ 1 200 samples) the worst-case compute time is
///   on the order of microseconds — far below the 1.5 s budget between ticks.
///
/// Confidence scaling:
///   Rules receive more data as the session progresses. <see cref="ConfidenceFromCoverage"/>
///   ensures a new insight starts with modest confidence and scales toward
///   the rule's configured maximum as the window fills.
/// </summary>
internal static class TrendAnalysis
{
    private const double PollIntervalSeconds = 1.5;

    // ── Slope (least-squares linear regression) ───────────────────────────────

    /// <summary>
    /// Computes the least-squares linear slope of a metric sample series.
    ///
    /// Uses the two-pass formula:
    ///   slope = (n·ΣxY − Σx·ΣY) / (n·Σx² − (Σx)²)
    /// where x = seconds since the first sample (avoids floating-point
    /// precision loss from large epoch timestamps).
    ///
    /// Returns slope in <em>metric-units per minute</em>:
    ///   positive = rising trend, negative = falling trend.
    ///
    /// Returns 0 when fewer than 3 samples are present (no meaningful fit).
    /// </summary>
    public static double SlopePerMinute(IReadOnlyList<MetricPoint> samples)
    {
        int n = samples.Count;
        if (n < 3) return 0;

        // Anchor to first sample to keep x values small (improves precision).
        double t0 = samples[0].Timestamp.ToUnixTimeMilliseconds() * 0.001;

        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;

        foreach (var pt in samples)
        {
            double x = pt.Timestamp.ToUnixTimeMilliseconds() * 0.001 - t0;
            double y = pt.Value;
            sumX  += x;
            sumY  += y;
            sumXY += x * y;
            sumX2 += x * x;
        }

        double denom = n * sumX2 - sumX * sumX;
        if (Math.Abs(denom) < 1e-10) return 0;

        double slopePerSecond = (n * sumXY - sumX * sumY) / denom;
        return slopePerSecond * 60.0;   // convert s⁻¹ → min⁻¹
    }

    // ── Window delta (multi-window comparison) ────────────────────────────────

    /// <summary>
    /// Returns the difference between the average of a recent short window and
    /// a longer historic window: <c>recentAvg − historicAvg</c>.
    ///
    /// A positive value means the metric has risen since the historic window began.
    /// Returns <see cref="double.NaN"/> when either window lacks sufficient data.
    /// </summary>
    public static double WindowDelta(
        ITelemetryHistoryBuffer history,
        TelemetryMetric         metric,
        TimeWindow              recentWindow,
        TimeWindow              historicWindow,
        int                     minRecentSamples   = 5,
        int                     minHistoricSamples = 20)
    {
        var recent   = history.GetStats(metric, recentWindow);
        var historic = history.GetStats(metric, historicWindow);

        if (recent.IsEmpty   || recent.SampleCount   < minRecentSamples)   return double.NaN;
        if (historic.IsEmpty || historic.SampleCount < minHistoricSamples) return double.NaN;

        return recent.Average - historic.Average;
    }

    // ── Confidence from window coverage ───────────────────────────────────────

    /// <summary>
    /// Returns a confidence score that scales linearly as data fills a time window.
    ///
    /// A rule that fires after 2 minutes of a 30-minute window gets ~61 % confidence;
    /// the same rule at a full 30-minute window gets the configured maximum.
    /// This prevents early findings from appearing overly certain.
    ///
    /// Formula: <c>min + (max − min) × coverage</c>
    ///   where coverage = actual samples / expected samples (capped at 1).
    /// </summary>
    public static int ConfidenceFromCoverage(
        int        sampleCount,
        TimeWindow window,
        int        min = 60,
        int        max = 92)
    {
        double expected = (int)window / PollIntervalSeconds;
        double coverage = Math.Min(1.0, sampleCount / expected);
        return (int)Math.Round(min + (max - min) * coverage);
    }

    // ── Spike episode detection ────────────────────────────────────────────────

    /// <summary>
    /// Counts distinct spike episodes in a sample series.
    ///
    /// A new episode begins the first time a sample exceeds <paramref name="threshold"/>.
    /// The episode ends when the metric stays below the threshold for longer than
    /// <paramref name="cooldownSeconds"/> — brief recoveries within the cooldown
    /// do not split an episode, preventing a single sustained spike from being
    /// counted as many.
    /// </summary>
    public static int CountSpikeEpisodes(
        IReadOnlyList<MetricPoint> samples,
        double                     threshold,
        double                     cooldownSeconds = 30)
    {
        if (samples.Count == 0) return 0;

        int    episodes    = 0;
        bool   inSpike     = false;
        double lastAboveTs = double.MinValue;

        foreach (var pt in samples)
        {
            double ts = pt.Timestamp.ToUnixTimeMilliseconds() * 0.001;

            if (pt.Value > threshold)
            {
                if (!inSpike)
                {
                    episodes++;
                    inSpike = true;
                }
                lastAboveTs = ts;
            }
            else if (inSpike && (ts - lastAboveTs) > cooldownSeconds)
            {
                inSpike = false;
            }
        }

        return episodes;
    }

    /// <summary>
    /// Returns true when spike episodes (samples above <paramref name="threshold"/>)
    /// are distributed across at least 2 of 3 equal thirds of the sample window.
    ///
    /// A true result indicates a recurring, spreading pattern rather than a
    /// one-time burst or an end-of-window cluster.
    /// Returns false when the window spans less than 90 seconds.
    /// </summary>
    public static bool SpikesAreDistributed(
        IReadOnlyList<MetricPoint> samples,
        double                     threshold)
    {
        if (samples.Count < 4) return false;

        double tStart = samples[0].Timestamp.ToUnixTimeMilliseconds() * 0.001;
        double tEnd   = samples[^1].Timestamp.ToUnixTimeMilliseconds() * 0.001;
        double span   = tEnd - tStart;

        if (span < 90) return false;

        double third  = span / 3.0;
        bool   first  = false;
        bool   middle = false;
        bool   last   = false;

        foreach (var pt in samples)
        {
            if (pt.Value <= threshold) continue;
            double offset = pt.Timestamp.ToUnixTimeMilliseconds() * 0.001 - tStart;
            if      (offset < third)      first  = true;
            else if (offset < 2 * third)  middle = true;
            else                          last   = true;
        }

        int covered = (first ? 1 : 0) + (middle ? 1 : 0) + (last ? 1 : 0);
        return covered >= 2;
    }

    // ── Formatting ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a human-readable elapsed duration derived from a sample count,
    /// suitable for use in insight detail text:
    ///   "45 s", "4 min", "12 min", "1h 4 min"
    /// </summary>
    public static string FormatElapsed(int sampleCount)
    {
        int seconds = (int)(sampleCount * PollIntervalSeconds);
        if (seconds < 90)    return $"{seconds} s";
        if (seconds < 3_600) return $"{seconds / 60} min";
        int h   = seconds / 3_600;
        int m   = (seconds % 3_600) / 60;
        return m == 0 ? $"{h}h" : $"{h}h {m} min";
    }
}
