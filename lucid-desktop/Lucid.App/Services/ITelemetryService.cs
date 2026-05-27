using Lucid.Helpers;

namespace Lucid.Services;

/// <summary>
/// Contract for the live hardware telemetry feed.
///
/// Implementations sample CPU, RAM, GPU, Disk, and thermal data on a
/// background thread and raise ReadingAvailable on the UI thread so
/// ViewModels can update ObservableObject properties directly without
/// additional dispatcher calls.
///
/// Lifetime:
///   The service is started once from AppServices.Initialize() and runs for
///   the application lifetime. Individual ViewModels subscribe/unsubscribe
///   to ReadingAvailable as they load and unload — they must NOT call
///   Start() or Stop() directly.
///
/// Back-navigation:
///   Use LastReading to seed ViewModel state immediately when a page is
///   navigated back to, avoiding a blank display until the next poll fires.
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Raised on the UI thread after each successful poll cycle.
    /// Safe to update ObservableObject properties directly from handlers.
    /// </summary>
    event EventHandler<TelemetrySnapshot>? ReadingAvailable;

    /// <summary>
    /// The most recent snapshot, or null before the first reading arrives.
    /// Subscribers should use this to initialize ViewModel state immediately
    /// when a page loads mid-session.
    /// </summary>
    TelemetrySnapshot? LastReading { get; }

    /// <summary>True while the background poll loop is active.</summary>
    bool IsRunning { get; }

    /// <summary>Starts the background polling loop. Called once from AppServices.</summary>
    void Start();

    /// <summary>Signals the poll loop to stop. Resources remain available.</summary>
    void Stop();
}
