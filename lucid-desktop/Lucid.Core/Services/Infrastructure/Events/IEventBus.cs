namespace Lucid.Services.Infrastructure.Events;

/// <summary>
/// Strongly-typed internal event bus for the Lucid platform.
///
/// Decouples subsystems by allowing publishers to emit events without knowing
/// which subscribers exist, and subscribers to react without holding direct
/// references to publishers.
///
/// Design constraints:
/// - No global mutable state outside this contract
/// - Handlers must be async-safe
/// - Handler failures must not propagate to publishers
/// - Subscriptions are cleaned up by disposing <see cref="EventSubscriptionToken"/>
///
/// Threading:
/// - <see cref="PublishAsync{TEvent}"/> is safe to call from any thread
/// - Handlers are invoked on the thread pool — never assume UI thread context
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publishes an event to all registered subscribers for <typeparamref name="TEvent"/>.
    ///
    /// Handlers are invoked in <see cref="EventPriority"/> descending order (Critical first).
    /// Handler failures are silently swallowed to protect the publishing path.
    /// </summary>
    /// <typeparam name="TEvent">Strongly-typed event derived from <see cref="OperationalEvent"/>.</typeparam>
    /// <param name="evt">The event to publish. Must be non-null.</param>
    /// <param name="dispatchMode">Controls sequential vs. concurrent vs. fire-and-forget delivery.</param>
    /// <param name="cancellationToken">Token to cancel pending handler invocations.</param>
    Task PublishAsync<TEvent>(
        TEvent            evt,
        EventDispatchMode dispatchMode      = EventDispatchMode.Sequential,
        CancellationToken cancellationToken = default)
        where TEvent : OperationalEvent;

    /// <summary>
    /// Subscribes a handler for events of type <typeparamref name="TEvent"/>.
    ///
    /// Returns a token that must be disposed to remove the subscription and
    /// prevent memory leaks. Store the token in a field and dispose it in
    /// the subscriber's shutdown/disposal path.
    /// </summary>
    /// <typeparam name="TEvent">Event type to subscribe to.</typeparam>
    /// <param name="handler">
    /// Async handler. Must not throw — unhandled exceptions are swallowed
    /// by the bus to protect other subscribers.
    /// </param>
    /// <param name="priority">
    /// Determines invocation order relative to other subscribers of the same
    /// event type. Higher priority runs first.
    /// </param>
    /// <returns>A disposable token. Dispose to unsubscribe.</returns>
    EventSubscriptionToken Subscribe<TEvent>(
        Func<TEvent, CancellationToken, Task> handler,
        EventPriority                         priority = EventPriority.Normal)
        where TEvent : OperationalEvent;

    /// <summary>
    /// Explicitly removes a subscription identified by its token.
    /// Equivalent to calling <see cref="EventSubscriptionToken.Dispose"/>.
    /// </summary>
    void Unsubscribe(EventSubscriptionToken token);

    /// <summary>
    /// Returns the number of active subscriptions for the given event type.
    /// Useful for diagnostics and testing.
    /// </summary>
    int GetSubscriberCount<TEvent>() where TEvent : OperationalEvent;
}
