namespace Lucid.Services.Infrastructure.Events;

/// <summary>
/// Controls how the event bus delivers an event to its subscribers.
/// </summary>
public enum EventDispatchMode
{
    /// <summary>
    /// Handlers are awaited sequentially in priority order.
    /// Safe for handlers that depend on ordering or shared state.
    /// Default for most operational events.
    /// </summary>
    Sequential = 0,

    /// <summary>
    /// Handlers are dispatched concurrently via Task.WhenAll.
    /// Use only when handlers are fully independent and order does not matter.
    /// Failures in one handler do not cancel others.
    /// </summary>
    Concurrent = 1,

    /// <summary>
    /// Handler is queued for background execution and the publish call returns
    /// immediately. Use for audit, telemetry, and non-critical side effects that
    /// must never slow down the publishing path.
    /// </summary>
    FireAndForget = 2,
}
