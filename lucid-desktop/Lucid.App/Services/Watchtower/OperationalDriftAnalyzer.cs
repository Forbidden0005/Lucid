using Lucid.Services.Analytics;

namespace Lucid.Services.Watchtower;

/// <summary>
/// Converts <see cref="LongTermMetricTrend"/> data from the historical analytics engine
/// into human-readable <see cref="DriftObservation"/> records for the Watchtower UI.
///
/// Drift is purely observational — no projections, no alarm language.
/// Changes ≥ 5% are considered significant.
/// </summary>
public static class OperationalDriftAnalyzer
{
    private const double SignificantDriftThreshold = 5.0;    // ≥5% change = significant
    private const double ElevatedDriftThreshold   = 18.0;   // ≥18% change = elevated

    /// <summary>
    /// Builds drift observations for CPU, RAM, and Disk from the given summary.
    /// Returns three entries in that order; entries with null averages reflect
    /// insufficient historical data.
    /// </summary>
    public static IReadOnlyList<DriftObservation> Analyze(HistoricalSummary summary) =>
    [
        BuildObservation(summary.CpuTrend,  "CPU",  ""),
        BuildObservation(summary.RamTrend,  "RAM",  ""),
        BuildObservation(summary.DiskTrend, "Disk", ""),
    ];

    private static DriftObservation BuildObservation(
        LongTermMetricTrend trend, string metricName, string glyph)
    {
        var direction     = ClassifyDirection(trend);
        var isSignificant = Math.Abs(trend.ChangePercent) >= SignificantDriftThreshold;

        return new DriftObservation(
            MetricName:   metricName,
            Glyph:        glyph,
            Direction:    direction,
            SevenDayAvg:  trend.SevenDayAvg,
            ThirtyDayAvg: trend.ThirtyDayAvg,
            ChangePercent: trend.ChangePercent,
            DriftLabel:   trend.TrendLabel,
            DriftColor:   DirectionToColor(direction),
            IsSignificant: isSignificant);
    }

    private static DriftDirection ClassifyDirection(LongTermMetricTrend trend)
    {
        if (trend.Direction == TrendDirection.Improving)
            return DriftDirection.Improving;

        if (trend.Direction == TrendDirection.Stable)
            return DriftDirection.Stable;

        // Worsening — classify by magnitude
        return trend.ChangePercent >= ElevatedDriftThreshold
            ? DriftDirection.Elevated
            : DriftDirection.Drifting;
    }

    private static string DirectionToColor(DriftDirection direction) => direction switch
    {
        DriftDirection.Improving => "#34D399",   // green
        DriftDirection.Stable    => "#6B7280",   // muted gray
        DriftDirection.Drifting  => "#F59E0B",   // amber
        DriftDirection.Elevated  => "#EF4444",   // red
        _                        => "#6B7280",
    };
}
