namespace Lucid.ViewModels;

/// <summary>
/// Kind of a chart marker — determines its color and icon in the chart.
/// </summary>
public enum ChartMarkerKind
{
    InsightOnset,    // The moment this finding first appeared
    InsightResolved, // The moment this finding cleared
    ActionExecuted,  // A remediation action was run
    ActionRollback,  // A rollback was executed
    SessionEvent,    // Boot, wake, or idle transition
}

/// <summary>
/// A vertical marker drawn on the chart at a specific point in time.
/// </summary>
public sealed record ChartMarker(
    DateTimeOffset OccurredAt,
    string         Label,
    ChartMarkerKind Kind);

/// <summary>
/// Complete data bundle for the expanded insight detail chart.
/// Built by <see cref="InsightDetailViewModel"/> from the telemetry history
/// buffer, machine baseline, and timeline event log.
///
/// All rendering state is pre-computed — the chart control does zero logic.
/// </summary>
public sealed class InsightDetailChartData
{
    // ── Series ────────────────────────────────────────────────────────────────

    /// <summary>Historical metric samples, oldest → newest. ≤100 downsampled points.</summary>
    public IReadOnlyList<SparklinePoint> History  { get; init; } = [];

    /// <summary>Projected future samples starting from the last history point.</summary>
    public IReadOnlyList<SparklinePoint> Forecast { get; init; } = [];

    // ── Baseline overlay ──────────────────────────────────────────────────────

    /// <summary>Machine-learned mean for this metric (optional).</summary>
    public double? BaselineMean   { get; init; }

    /// <summary>Machine-learned standard deviation (optional). Band = ±1σ.</summary>
    public double? BaselineStdDev { get; init; }

    // ── Markers ───────────────────────────────────────────────────────────────

    /// <summary>Vertical event markers drawn on the chart.</summary>
    public IReadOnlyList<ChartMarker> Markers { get; init; } = [];

    // ── Presentation ──────────────────────────────────────────────────────────

    public SparklineColorState ColorState { get; init; }

    /// <summary>"%" for hardware percentages, "°C" for temperature.</summary>
    public string UnitLabel  { get; init; } = "%";

    /// <summary>Display name for the metric, e.g. "CPU Usage".</summary>
    public string MetricName { get; init; } = "";

    // ── Pre-computed statistics (for the stat row below the chart) ────────────

    public double CurrentVal  { get; init; }
    public double AverageVal  { get; init; }
    public double MinVal      { get; init; }
    public double MaxVal      { get; init; }
    public double SlopePerMin { get; init; }

    // ── Axis range (may be wider than the data range for visual clarity) ──────

    /// <summary>Lowest value on the Y axis (inclusive).</summary>
    public double AxisMin { get; init; }

    /// <summary>Highest value on the Y axis (inclusive).</summary>
    public double AxisMax { get; init; }
}
