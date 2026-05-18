using ExplainMyPC.Services.Intelligence;
using ExplainMyPC.Services.Telemetry;

namespace ExplainMyPC.ViewModels;

/// <summary>
/// Builds <see cref="SparklineData"/> for insight cards by querying the live
/// telemetry history buffer, machine baseline, and computing a linear trend.
///
/// Metric mapping (insight ID → TelemetryMetric):
///   cpu.*           → Cpu          (sustained-high, escalating, periodic-spikes)
///   cpu.high-temp / cpu.thermal-trend → Temperature
///   ram.*           → Ram
///   gpu.*           → Gpu
///   disk.*          → Disk
///   system.healthy  → Cpu (shown as green)
///   forecast.* / synthesis.* / session.* → null (no sparkline)
///
/// Performance:
///   O(n) over ≤200 samples per call. Safe to call on the UI thread from
///   InsightCardViewModel.Update() — well within the 1.5 s tick budget.
///   Zero allocations during canvas rendering; all brushes are pre-allocated.
///
/// TrendAnalysis note:
///   TrendAnalysis is declared `internal` in Services.Intelligence but lives
///   in the same assembly (ExplainMyPC.App), so it is accessible here.
/// </summary>
public static class TrendVisualizationHelper
{
    // ── Metric mapping ────────────────────────────────────────────────────────

    /// <summary>
    /// Maps an insight rule ID to the telemetry metric it monitors.
    /// Internal so <see cref="InsightDetailViewModel"/> can reuse the mapping.
    /// Returns null for forecast, synthesis, session, and healthy insights.
    /// </summary>
    internal static TelemetryMetric? MetricFor(string id) => id switch
    {
        "cpu.sustained-high"         => TelemetryMetric.Cpu,
        "cpu.escalating"             => TelemetryMetric.Cpu,
        "cpu.periodic-spikes"        => TelemetryMetric.Cpu,
        "cpu.high-temperature"       => TelemetryMetric.Temperature,
        "cpu.thermal-trend"          => TelemetryMetric.Temperature,
        "ram.high-pressure"          => TelemetryMetric.Ram,
        "ram.rising-trend"           => TelemetryMetric.Ram,
        "gpu.unexpected-load"        => TelemetryMetric.Gpu,
        "disk.low-space"             => TelemetryMetric.Disk,
        "disk.sustained-io"          => TelemetryMetric.Disk,
        "disk.long-running-pressure" => TelemetryMetric.Disk,
        "system.healthy"             => TelemetryMetric.Cpu,
        _                            => null,  // forecast.*, synthesis.*, session.*, baseline.* — no sparkline
    };

    // ── Build ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="SparklineData"/> for the given insight rule, or
    /// returns <c>null</c> when no metric mapping exists (forecast, synthesis,
    /// session, and baseline-only insights have no sparkline).
    /// </summary>
    public static SparklineData? Build(string insightId, InsightSeverity severity)
    {
        var metric = MetricFor(insightId);
        if (metric is null) return null;

        // Query the last 5 minutes from the rolling buffer (oldest → newest).
        var rawSamples = AppServices.History.GetSamples(metric.Value, TimeWindow.LastFiveMinutes);
        if (rawSamples.Count < 3) return null;

        // Convert MetricPoints to SparklinePoints.
        var allPoints = new List<SparklinePoint>(rawSamples.Count);
        foreach (var pt in rawSamples)
            allPoints.Add(new SparklinePoint(pt.Timestamp, pt.Value));

        // Downsample to ≤60 points so the canvas render stays O(1).
        var history = Downsample(allPoints, maxPoints: 60);

        // Linear slope for forecast projection and trend summary.
        double slope = TrendAnalysis.SlopePerMinute(rawSamples);

        // 2-minute forward projection — only rendered when the trend is meaningful.
        var forecast = BuildForecast(history, slope, forecastMinutes: 2);

        // Baseline band from the adaptive machine learning service.
        string unit           = metric.Value == TelemetryMetric.Temperature ? "°C" : "%";
        double? baselineMean   = null;
        double? baselineStdDev = null;

        var baseline = AppServices.Baseline.CurrentBaseline;
        if (baseline is not null)
        {
            (baselineMean, baselineStdDev) = metric.Value switch
            {
                TelemetryMetric.Cpu         => (baseline.IdleCpuReady     ? (double?)baseline.IdleCpuMean      : null,
                                                baseline.IdleCpuReady     ? (double?)baseline.IdleCpuStdDev    : null),
                TelemetryMetric.Ram         => (baseline.NormalRamReady   ? (double?)baseline.NormalRamMean     : null,
                                                baseline.NormalRamReady   ? (double?)baseline.NormalRamStdDev   : null),
                TelemetryMetric.Temperature => (baseline.NormalThermalReady ? (double?)baseline.NormalThermalMean   : null,
                                                baseline.NormalThermalReady ? (double?)baseline.NormalThermalStdDev : null),
                _                           => ((double?)null, (double?)null),
            };
        }

        // Color state from insight severity; system.healthy gets green.
        var colorState = insightId == "system.healthy"
            ? SparklineColorState.Good
            : severity switch
            {
                InsightSeverity.Warning        => SparklineColorState.Warning,
                InsightSeverity.Recommendation => SparklineColorState.Monitoring,
                _                              => SparklineColorState.Monitoring,
            };

        return new SparklineData
        {
            History        = history,
            Forecast       = forecast,
            BaselineMean   = baselineMean,
            BaselineStdDev = baselineStdDev,
            ColorState     = colorState,
            TrendSummary   = BuildTrendSummary(rawSamples, unit),
            UnitLabel      = unit,
        };
    }

    // ── Downsampling ──────────────────────────────────────────────────────────

    /// <summary>
    /// Evenly picks at most <paramref name="maxPoints"/> from the source list
    /// using round-based index selection (no averaging — preserves real values).
    /// </summary>
    private static IReadOnlyList<SparklinePoint> Downsample(
        List<SparklinePoint> points, int maxPoints)
    {
        if (points.Count <= maxPoints) return points;

        var result = new List<SparklinePoint>(maxPoints);
        double step = (double)(points.Count - 1) / (maxPoints - 1);

        for (int i = 0; i < maxPoints; i++)
        {
            int idx = (int)Math.Round(i * step);
            result.Add(points[Math.Min(idx, points.Count - 1)]);
        }

        return result;
    }

    // ── Forecast projection ───────────────────────────────────────────────────

    /// <summary>
    /// Extrapolates from the last history point at one point per 15 s.
    /// Returns an empty list when slope is negligible (|slope| &lt; 0.1 %/min).
    /// </summary>
    private static IReadOnlyList<SparklinePoint> BuildForecast(
        IReadOnlyList<SparklinePoint> history,
        double slopePerMinute,
        int forecastMinutes)
    {
        if (history.Count == 0 || Math.Abs(slopePerMinute) < 0.1)
            return [];

        var last  = history[^1];
        int steps = forecastMinutes * 4;   // one point per 15 s
        var result = new List<SparklinePoint>(steps + 1);
        result.Add(last);  // connect from the last real measurement

        for (int i = 1; i <= steps; i++)
        {
            double minutesAhead = i / 4.0;
            double projected    = Math.Clamp(last.Value + slopePerMinute * minutesAhead, 0, 200);
            result.Add(new SparklinePoint(last.Timestamp.AddSeconds(i * 15), projected));
        }

        return result;
    }

    // ── Trend summary text ────────────────────────────────────────────────────

    /// <summary>
    /// Computes a concise trend description from the oldest → newest delta:
    ///   "▲ 18% over 5 min", "▼ 4% over 2 min", "Stable", "+3.2°C over 4 min".
    /// Returns an empty string when the window is shorter than 30 s.
    /// </summary>
    private static string BuildTrendSummary(
        IReadOnlyList<MetricPoint> samples,
        string unit)
    {
        if (samples.Count < 5) return string.Empty;

        double delta = samples[^1].Value - samples[0].Value;
        var    span  = samples[^1].Timestamp - samples[0].Timestamp;

        if (span.TotalSeconds < 30) return string.Empty;

        int    windowMins = (int)Math.Round(span.TotalMinutes);
        string window     = windowMins <= 1 ? "1 min" : $"{windowMins} min";

        if (Math.Abs(delta) < 1.5) return "Stable";

        if (unit == "°C")
        {
            string sign = delta > 0 ? "+" : "";
            return $"{sign}{delta:F1}°C over {window}";
        }
        else
        {
            string arrow = delta > 0 ? "▲" : "▼";
            return $"{arrow} {Math.Abs(delta):F0}% over {window}";
        }
    }
}
