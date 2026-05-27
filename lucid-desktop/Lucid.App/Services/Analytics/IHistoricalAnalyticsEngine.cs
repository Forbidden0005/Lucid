namespace Lucid.Services.Analytics;

/// <summary>
/// Computes long-term operational intelligence from the SQLite historical database.
///
/// All computation is:
///   • Deterministic — same inputs always produce the same output
///   • Local-only — no cloud, no telemetry upload
///   • Confidence-aware — results include data coverage metadata
///   • Non-blocking — all heavy queries run asynchronously
///
/// The engine does NOT maintain subscriptions or live state.
/// Call <see cref="ComputeAsync"/> on demand (when the Historical page opens
/// or when the user explicitly requests a refresh).
/// </summary>
public interface IHistoricalAnalyticsEngine
{
    /// <summary>
    /// Computes a complete <see cref="HistoricalSummary"/> from persisted data.
    ///
    /// Returns a minimal "cold-start" summary (all counts zero, null dates)
    /// when the database is empty or the engine is unhealthy.
    ///
    /// Typically completes in &lt; 200 ms. Safe to call from any thread.
    /// </summary>
    Task<HistoricalSummary> ComputeAsync();

    /// <summary>
    /// The most recently computed summary, or null if <see cref="ComputeAsync"/>
    /// has not been called yet.
    /// </summary>
    HistoricalSummary? LastSummary { get; }
}
