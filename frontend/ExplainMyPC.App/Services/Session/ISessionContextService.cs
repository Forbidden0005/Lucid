namespace ExplainMyPC.Services.Session;

/// <summary>
/// Tracks operational session state and publishes a <see cref="SessionContext"/>
/// snapshot whenever that state changes.
///
/// Tracked dimensions:
///   System boot time   — derived from <see cref="Environment.TickCount64"/>.
///   Session start      — time ExplainMyPC was launched (user-session proxy).
///   Sleep/wake events  — detected via gaps in telemetry tick timing.
///   Idle/active state  — CPU below 15 % for ≥ 3 continuous minutes = idle.
///   Insight onsets     — first-appearance times for each active finding.
///
/// Threading:
///   All state mutations and event notifications occur on the UI thread,
///   inherited from <see cref="ITelemetryService.ReadingAvailable"/> and
///   <see cref="ISystemInsightEngine.InsightsUpdated"/> which are already
///   marshalled to the UI thread by their respective services.
///
///   <see cref="Current"/> is safe to read from the UI thread without locking.
///
/// Lifecycle:
///   Call <see cref="Start"/> after the intelligence engine has started.
///   Call <see cref="Stop"/> from AppServices.Shutdown().
/// </summary>
public interface ISessionContextService
{
    /// <summary>
    /// The most recent session context snapshot.
    /// Rebuilt on every state transition (wake, idle change, insight change).
    /// Never null after construction.
    /// </summary>
    SessionContext Current { get; }

    /// <summary>
    /// Raised on the UI thread whenever the session context snapshot changes.
    /// Fires much less frequently than telemetry ticks — only on transitions.
    /// </summary>
    event EventHandler<SessionContext>? SessionContextChanged;

    /// <summary>
    /// Subscribes to the telemetry and intelligence engine feeds.
    /// Call once from AppServices.Initialize(), after both services have started.
    /// </summary>
    void Start();

    /// <summary>
    /// Unsubscribes from all upstream services.
    /// Call from AppServices.Shutdown().
    /// </summary>
    void Stop();
}
