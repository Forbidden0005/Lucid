namespace Lucid.Services.Intelligence;

/// <summary>
/// Coordinates a set of <see cref="IInsightRule"/> heuristics and publishes
/// the resulting findings to the rest of the application.
///
/// The engine subscribes to <see cref="ITelemetryService.ReadingAvailable"/>
/// and evaluates all rules on every poll tick. <see cref="InsightsUpdated"/>
/// is raised only when the active set of findings changes (a new finding
/// appears, an existing one clears, or a severity changes), avoiding
/// unnecessary UI re-renders on every 1.5-second tick.
///
/// Threading:
///   All events fire on the UI thread (via the telemetry service's
///   DispatcherQueue delivery). Subscribers can update ObservableObject
///   properties directly without additional dispatching.
/// </summary>
public interface ISystemInsightEngine : IDisposable
{
    /// <summary>
    /// Raised on the UI thread when the active set of findings changes.
    /// The list is ordered: highest severity first, then informational.
    /// </summary>
    event EventHandler<IReadOnlyList<SystemInsight>>? InsightsUpdated;

    /// <summary>
    /// The most recent evaluated findings, refreshed every poll tick.
    /// Empty before the first telemetry reading arrives.
    /// </summary>
    IReadOnlyList<SystemInsight> CurrentInsights { get; }

    /// <summary>Begins evaluating rules on each telemetry tick.</summary>
    void Start();

    /// <summary>Stops evaluation. Resources remain available for restart.</summary>
    void Stop();
}
