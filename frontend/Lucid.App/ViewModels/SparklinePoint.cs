namespace Lucid.ViewModels;

/// <summary>
/// A single timestamped value in a sparkline history or forecast series.
///
/// Kept as a readonly record struct so large arrays of points cost only
/// 16 bytes each with zero heap allocation per point.
/// </summary>
public readonly record struct SparklinePoint(DateTimeOffset Timestamp, double Value);
