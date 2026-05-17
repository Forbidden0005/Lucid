namespace ExplainMyPC.Services.Telemetry;

/// <summary>
/// The hardware metric tracked in the telemetry history buffer.
/// </summary>
public enum TelemetryMetric
{
    Cpu,
    Ram,
    Gpu,
    Disk
}

/// <summary>
/// A named time window for history queries.
///
/// The underlying value is the window duration in seconds, which lets
/// callers convert without a lookup table:
/// <c>TimeSpan.FromSeconds((int)window)</c>
/// </summary>
public enum TimeWindow
{
    LastMinute        =      60,
    LastFiveMinutes   =     300,
    LastThirtyMinutes = 1_800
}

/// <summary>
/// A single timestamped measurement from the history buffer.
/// </summary>
public readonly record struct MetricPoint(DateTimeOffset Timestamp, double Value);

/// <summary>
/// Aggregated statistics computed over a set of history samples.
/// </summary>
/// <param name="Min">Lowest value seen within the window.</param>
/// <param name="Max">Highest value seen within the window.</param>
/// <param name="Average">Mean value, rounded to one decimal place.</param>
/// <param name="Latest">Most recent recorded value.</param>
/// <param name="SampleCount">Number of samples contributing to these statistics.</param>
public readonly record struct MetricStats(
    double Min,
    double Max,
    double Average,
    double Latest,
    int    SampleCount)
{
    /// <summary>True when no samples were found for the requested window.</summary>
    public bool IsEmpty => SampleCount == 0;
}
