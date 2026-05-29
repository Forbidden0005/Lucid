namespace Lucid.Services.Infrastructure.Events;

/// <summary>
/// Represents an active event bus subscription.
///
/// Disposing this token removes the associated handler from the event bus,
/// preventing memory leaks in long-lived applications.
///
/// Usage pattern:
/// <code>
///   using var token = _eventBus.Subscribe&lt;InsightGeneratedEvent&gt;(OnInsight);
///   // or store in a field and dispose in Shutdown()
/// </code>
/// </summary>
public sealed class EventSubscriptionToken : IDisposable
{
    private readonly Action  _unsubscribe;
    private          bool    _disposed;

    /// <summary>The event type this token is registered for.</summary>
    public Type EventType { get; }

    /// <summary>Unique identifier for this subscription instance.</summary>
    public Guid SubscriptionId { get; } = Guid.NewGuid();

    internal EventSubscriptionToken(Type eventType, Action unsubscribe)
    {
        EventType    = eventType;
        _unsubscribe = unsubscribe;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _unsubscribe();
    }
}
