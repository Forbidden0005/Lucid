namespace ExplainMyPC.ViewModels;

/// <summary>
/// Adaptive color state that drives the sparkline line, area fill, and forecast colors.
/// </summary>
public enum SparklineColorState
{
    Good,        // Green  #57D68D — system.healthy
    Monitoring,  // Blue   #4EA1FF — Info severity
    Warning,     // Orange #FFB347 — Warning severity
    Critical,    // Red    #FF5050 — Critical severity (reserved)
}

/// <summary>
/// Immutable data bundle passed to <see cref="ExplainMyPC.Views.SparklineControl"/>
/// for rendering. Built by <see cref="TrendVisualizationHelper.Build"/> from live
/// telemetry history, machine baseline, and insight severity.
///
/// All rendering state is pre-computed here; the control does zero logic —
/// it maps these values straight to canvas coordinates.
/// </summary>
public sealed class SparklineData
{
    /// <summary>
    /// Historical metric samples in chronological order (oldest → newest).
    /// Already downsampled to ≤60 points for O(1) rendering cost.
    /// </summary>
    public IReadOnlyList<SparklinePoint> History { get; init; } = [];

    /// <summary>
    /// Projected future samples starting from the last history point.
    /// Empty when the slope is near zero (stable metric — no projection needed).
    /// </summary>
    public IReadOnlyList<SparklinePoint> Forecast { get; init; } = [];

    /// <summary>
    /// Machine baseline mean for this metric (optional).
    /// When set, the control renders a ±1σ reference band.
    /// </summary>
    public double? BaselineMean { get; init; }

    /// <summary>
    /// Machine baseline standard deviation (optional).
    /// Used to set the height of the baseline band = ±1σ.
    /// </summary>
    public double? BaselineStdDev { get; init; }

    /// <summary>Adaptive color state for line, fill, and forecast visuals.</summary>
    public SparklineColorState ColorState { get; init; }

    /// <summary>
    /// Human-readable trend summary displayed below the sparkline.
    /// Examples: "▲ 18% over 5 min", "Stable", "+3.2°C over 4 min".
    /// Empty string when insufficient history is available.
    /// </summary>
    public string TrendSummary { get; init; } = string.Empty;

    /// <summary>Unit abbreviation for axis context — "%" or "°C".</summary>
    public string UnitLabel { get; init; } = "%";
}
