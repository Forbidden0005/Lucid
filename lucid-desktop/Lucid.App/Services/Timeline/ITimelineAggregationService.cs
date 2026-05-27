namespace Lucid.Services.Timeline;

/// <summary>
/// Aggregates events from all Lucid subsystems into a unified chronological stream.
///
/// Sources (all four produce <see cref="TimelineEvent"/> instances):
///   • Intelligence engine — insight onset/clear, forecast alerts, synthesis correlations
///   • Session context     — system boot, wake from sleep, idle begin/end
///   • Narrative engine    — checkpoint events when health state changes materially
///   • Operation history   — executed actions, previews, rollbacks (loaded on Start)
///
/// The service maintains an in-memory ring buffer of up to 500 events, newest-first.
/// It does NOT persist events to disk; the timeline is rebuilt from live state on each
/// app launch. Operation history records (persistent) are loaded from the JSON file at
/// startup via <see cref="Services.History.IOperationHistoryService"/>.
///
/// Threading:
///   All live event callbacks arrive on the UI thread (inherited from their upstream
///   sources). <see cref="Events"/> is therefore safe to read from the UI thread.
///   History load results are marshalled back to the UI thread via DispatcherQueue.
///
/// Lifecycle:
///   Call <see cref="Start"/> from AppServices.Initialize after all dependent services
///   have started. Call <see cref="Stop"/> from AppServices.Shutdown before stopping
///   the intelligence, session, or narrative services (which this service subscribes to).
/// </summary>
public interface ITimelineAggregationService
{
    /// <summary>
    /// All events in newest-first order. Capped at 500 entries.
    /// Safe to read from the UI thread without locking.
    /// </summary>
    IReadOnlyList<TimelineEvent> Events { get; }

    /// <summary>
    /// Raised on the UI thread whenever a new event is prepended to <see cref="Events"/>.
    /// The argument is the newly-added event.
    /// Fires for both live events and history records loaded on startup.
    /// </summary>
    event EventHandler<TimelineEvent>? NewEventAdded;

    /// <summary>
    /// Subscribes to upstream feeds (intelligence, session, narrative) and seeds
    /// from current state (existing session events and currently active insights).
    /// Also fires off an async history load that delivers records via
    /// <see cref="NewEventAdded"/> once the file read completes.
    /// </summary>
    void Start();

    /// <summary>
    /// Unsubscribes from all upstream feeds. Call before stopping the services
    /// this aggregator depends on.
    /// </summary>
    void Stop();

    /// <summary>
    /// Adds an externally-produced event to the timeline.
    /// Used by services (e.g. AutomationOrchestrator) that produce events
    /// outside the four standard sources. The event is prepended to the ring
    /// buffer and <see cref="NewEventAdded"/> is raised on the UI thread.
    /// Thread-safe — marshals to UI thread if called off-thread.
    /// </summary>
    void AddExternalEvent(TimelineEvent ev);
}
