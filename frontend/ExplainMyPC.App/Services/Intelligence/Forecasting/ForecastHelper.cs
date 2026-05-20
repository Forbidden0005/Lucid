using ExplainMyPC.Services.Telemetry;

namespace ExplainMyPC.Services.Intelligence.Forecasting;

/// <summary>
/// Deterministic forecasting helpers for predictive insight rules.
///
/// All methods are pure — no state, no I/O, no allocations — and run
/// synchronously on the UI thread inside <see cref="IInsightRule.Evaluate"/>.
///
/// Forecasting model:
///   All estimates use linear extrapolation of the current trend slope.
///   This is intentionally simple and honest: real-world metrics are not
///   strictly linear, so estimates carry appropriate uncertainty. Confidence
///   scoring accounts for both history coverage and forecast horizon length.
///
/// Design principle:
///   Rules should produce forecasts only when the signal is strong enough to
///   be actionable. A 96-hour disk-exhaustion forecast is not worth surfacing;
///   a 4-hour one is. Each rule defines its own horizon gate.
/// </summary>
internal static class ForecastHelper
{
    // ── Time-to-threshold ─────────────────────────────────────────────────────

    /// <summary>
    /// Estimates how many minutes at <paramref name="slopePerMinute"/> until
    /// <paramref name="currentValue"/> reaches <paramref name="threshold"/>.
    ///
    /// Returns <see cref="double.PositiveInfinity"/> when slope ≤ 0 (no convergence)
    /// or when the threshold is already exceeded.
    /// </summary>
    public static double MinutesToThreshold(
        double currentValue,
        double slopePerMinute,
        double threshold)
    {
        if (slopePerMinute <= 0) return double.PositiveInfinity;

        double remaining = threshold - currentValue;
        if (remaining <= 0) return 0;   // already at or past threshold

        return remaining / slopePerMinute;
    }

    // ── Disk-specific helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Estimates hours until the disk is full, based on current free space and
    /// the current sustained write rate.
    ///
    /// Returns <see cref="double.PositiveInfinity"/> when writeMbps is ≤ 0.
    ///
    /// Note: This estimate is only reliable when writeMbps reflects a genuinely
    /// sustained transfer — short bursts produce inflated results. Callers should
    /// confirm the write rate has been elevated across a multi-minute window before
    /// using this method.
    /// </summary>
    public static double HoursUntilDiskFullFromWriteRate(double freeGb, double writeMbps)
    {
        if (writeMbps <= 0) return double.PositiveInfinity;

        // Convert MB/s → GB/hour: MB/s × 3600 s/hr ÷ 1024 MB/GB
        double gbPerHour = writeMbps * 3600.0 / 1024.0;
        return freeGb / gbPerHour;
    }

    /// <summary>
    /// Converts a disk-usage % slope (from the history buffer) to an estimated
    /// number of hours until the disk reaches <paramref name="targetPct"/>.
    ///
    /// Returns <see cref="double.PositiveInfinity"/> when slope ≤ 0.
    /// </summary>
    public static double HoursUntilDiskThreshold(
        double currentDiskPct,
        double slopePctPerMinute,
        double targetPct = 95.0)
    {
        if (slopePctPerMinute <= 0) return double.PositiveInfinity;
        double remaining = targetPct - currentDiskPct;
        if (remaining <= 0) return 0;
        return remaining / slopePctPerMinute / 60.0;   // min → hours
    }

    // ── Startup impact estimation ─────────────────────────────────────────────

    /// <summary>
    /// Estimates the number of minutes a machine spends initializing startup
    /// applications after Windows login, based on the composition of the
    /// startup list.
    ///
    /// Model (conservative, based on observed behavior of common app categories):
    ///   High-impact app   (Electron / browser / launcher) : +2.0 min each
    ///   Moderate-impact app (sync client / peripheral mgr): +0.8 min each
    ///   Any other startup entry                           : +0.2 min each
    ///
    /// The result is the estimated "settling delay" — how long after login
    /// before the system reaches responsive idle. This is additive and ignores
    /// parallelism, so it intentionally overestimates slightly to avoid
    /// making the forecast feel too optimistic.
    /// </summary>
    public static double EstimateStartupSettlingMinutes(
        int highImpactCount,
        int moderateImpactCount,
        int totalCount)
    {
        // Low-impact = total minus the other two categories
        int otherCount = Math.Max(0, totalCount - highImpactCount - moderateImpactCount);

        double estimate = (highImpactCount * 2.0)
                        + (moderateImpactCount * 0.8)
                        + (otherCount * 0.2);

        // Cap to avoid absurdly large numbers (> 20 apps would be 40+ min)
        return Math.Min(estimate, 25.0);
    }

    // ── Human-readable time formatting ────────────────────────────────────────

    /// <summary>
    /// Formats a minute count as a human-readable time horizon.
    ///   &lt; 5 min   → "within a few minutes"
    ///   5–90 min  → "approximately {N} minutes"
    ///   90+       → "approximately {N} hours"
    ///
    /// Intended for forecast detail text (e.g., "disk could be full in approximately 3 hours").
    /// </summary>
    public static string FormatMinuteHorizon(double minutes)
    {
        if (minutes < 5)    return "within a few minutes";
        if (minutes < 90)   return $"approximately {(int)Math.Round(minutes)} minutes";
        double hours = minutes / 60.0;
        return $"approximately {hours:F1} hours";
    }

    /// <summary>
    /// Formats an hour count as a human-readable time horizon.
    ///   &lt; 1 h    → "less than an hour"
    ///   1–24 h   → "approximately {N} hours"
    ///   24+ h    → "approximately {N} days"
    /// </summary>
    public static string FormatHourHorizon(double hours)
    {
        if (hours < 1)    return "less than an hour";
        if (hours < 24)   return $"approximately {hours:F1} hours";
        return $"approximately {(int)Math.Round(hours / 24)} day{((int)(hours / 24) == 1 ? "" : "s")}";
    }

    // ── Forecast confidence ───────────────────────────────────────────────────

    /// <summary>
    /// Returns a confidence percentage for a linear-extrapolation forecast.
    ///
    /// Two factors reduce confidence:
    ///   1. Sparse history — fewer samples mean a noisier slope estimate.
    ///   2. Long time horizon — the further into the future, the larger the
    ///      accumulated uncertainty from non-linear real-world effects.
    ///
    /// Base confidence is derived from history window coverage (same formula as
    /// <see cref="TrendAnalysis.ConfidenceFromCoverage"/>), then a horizon
    /// discount of 1 point per 3 minutes is applied, clamped to [minConf, maxConf].
    /// </summary>
    public static int ForecastConfidence(
        int        sampleCount,
        TimeWindow window,
        double     minutesToEvent,
        int        minConf = 60,
        int        maxConf = 88)
    {
        // Base: from history coverage (60 % at minimum data, 85 % at full window)
        int baseCovConf = TrendAnalysis.ConfidenceFromCoverage(
            sampleCount, window, min: 60, max: 85);

        // Horizon discount: -1% per 3 minutes of forecast horizon
        int horizonPenalty = (int)(minutesToEvent / 3.0);

        return Math.Clamp(baseCovConf - horizonPenalty, minConf, maxConf);
    }
}
