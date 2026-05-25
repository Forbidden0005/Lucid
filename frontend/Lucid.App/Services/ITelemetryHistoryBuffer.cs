using Lucid.Helpers;
using Lucid.Services.Telemetry;

namespace Lucid.Services;

/// <summary>
/// Rolling in-memory store for hardware telemetry time-series.
///
/// Maintains a fixed-capacity circular buffer covering up to the last
/// 30 minutes of CPU, RAM, GPU, and Disk usage at the telemetry service's
/// native poll cadence (~1.5 s).
///
/// Designed to feed:
///   • Trend sparklines on the dashboard
///   • Anomaly detection in the intelligence engine
///   • "Explain My PC" natural-language analysis
///
/// Thread safety:
///   Record() is typically called on the UI thread (via DispatcherQueue).
///   GetSamples() and GetStats() may be called from any thread concurrently;
///   implementations must guard accordingly.
/// </summary>
public interface ITelemetryHistoryBuffer
{
    /// <summary>
    /// Appends a snapshot to the rolling buffer.
    /// Older entries are automatically evicted when capacity is reached.
    /// </summary>
    void Record(TelemetrySnapshot snapshot);

    /// <summary>
    /// Returns all samples for <paramref name="metric"/> that fall within
    /// <paramref name="window"/>, ordered from oldest to newest.
    /// Returns an empty list when no data has been recorded yet.
    /// </summary>
    IReadOnlyList<MetricPoint> GetSamples(TelemetryMetric metric, TimeWindow window);

    /// <summary>
    /// Returns aggregated min / max / average / latest statistics for
    /// <paramref name="metric"/> over <paramref name="window"/>.
    /// Check <see cref="MetricStats.IsEmpty"/> before using the result.
    /// </summary>
    MetricStats GetStats(TelemetryMetric metric, TimeWindow window);

    /// <summary>Maximum number of samples the buffer can hold.</summary>
    int Capacity { get; }

    /// <summary>Number of samples currently stored (0 to Capacity).</summary>
    int Count { get; }
}
