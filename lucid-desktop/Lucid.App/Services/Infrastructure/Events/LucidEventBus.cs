using System.Collections.Concurrent;

namespace Lucid.Services.Infrastructure.Events;

/// <summary>
/// Production implementation of <see cref="IEventBus"/>.
///
/// Thread-safety model:
/// - Subscription list mutations use a lock per event type.
/// - PublishAsync takes a snapshot of handlers before invoking them, so
///   subscribe/unsubscribe during dispatch is safe and takes effect on the
///   next publish cycle.
/// - No global lock is held during handler invocations.
///
/// Memory model:
/// - Subscriptions use strong references. Callers must dispose tokens to
///   release handler delegates and prevent memory leaks.
/// - The bus itself holds no references to the objects that subscribe;
///   only to the handler delegates they provide.
///
/// Failure model:
/// - Handler exceptions are caught individually and discarded.
/// - A failing handler never prevents other handlers from receiving the event.
/// - A failing handler never propagates back to the publisher.
/// </summary>
public sealed class LucidEventBus : IEventBus, IDisposable
{
    // Per-event-type subscription bucket.
    private readonly ConcurrentDictionary<Type, SubscriptionBucket> _buckets = new();
    private          bool _disposed;

    // ── IEventBus ──────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public EventSubscriptionToken Subscribe<TEvent>(
        Func<TEvent, CancellationToken, Task> handler,
        EventPriority                         priority = EventPriority.Normal)
        where TEvent : OperationalEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        var bucket = _buckets.GetOrAdd(typeof(TEvent), _ => new SubscriptionBucket());
        return bucket.Add(handler, priority);
    }

    /// <inheritdoc />
    public void Unsubscribe(EventSubscriptionToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        token.Dispose();
    }

    /// <inheritdoc />
    public async Task PublishAsync<TEvent>(
        TEvent            evt,
        EventDispatchMode dispatchMode      = EventDispatchMode.Sequential,
        CancellationToken cancellationToken = default)
        where TEvent : OperationalEvent
    {
        if (_disposed) return;
        ArgumentNullException.ThrowIfNull(evt);

        if (!_buckets.TryGetValue(typeof(TEvent), out var bucket))
            return; // No subscribers — fast path.

        // Snapshot handlers before invocation so subscribe/unsubscribe
        // during dispatch is safe.
        var handlers = bucket.Snapshot();
        if (handlers.Length == 0) return;

        switch (dispatchMode)
        {
            case EventDispatchMode.Sequential:
                await InvokeSequentialAsync(handlers, evt, cancellationToken).ConfigureAwait(false);
                break;

            case EventDispatchMode.Concurrent:
                await InvokeConcurrentAsync(handlers, evt, cancellationToken).ConfigureAwait(false);
                break;

            case EventDispatchMode.FireAndForget:
                _ = Task.Run(
                    () => InvokeSequentialAsync(handlers, evt, CancellationToken.None),
                    CancellationToken.None);
                break;
        }
    }

    /// <inheritdoc />
    public int GetSubscriberCount<TEvent>() where TEvent : OperationalEvent =>
        _buckets.TryGetValue(typeof(TEvent), out var bucket) ? bucket.Count : 0;

    // ── Invocation helpers ────────────────────────────────────────────────────

    private static async Task InvokeSequentialAsync<TEvent>(
        HandlerEntry[]    handlers,
        TEvent            evt,
        CancellationToken ct)
        where TEvent : OperationalEvent
    {
        foreach (var entry in handlers)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await ((Func<TEvent, CancellationToken, Task>)entry.Handler)(evt, ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Swallow — handler failures must not cascade.
            }
        }
    }

    private static async Task InvokeConcurrentAsync<TEvent>(
        HandlerEntry[]    handlers,
        TEvent            evt,
        CancellationToken ct)
        where TEvent : OperationalEvent
    {
        var tasks = new Task[handlers.Length];
        for (int i = 0; i < handlers.Length; i++)
        {
            var entry = handlers[i];
            tasks[i] = InvokeSafeAsync(entry, evt, ct);
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task InvokeSafeAsync<TEvent>(
        HandlerEntry      entry,
        TEvent            evt,
        CancellationToken ct)
        where TEvent : OperationalEvent
    {
        try
        {
            await ((Func<TEvent, CancellationToken, Task>)entry.Handler)(evt, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            // Swallow — independent of other concurrent handlers.
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _buckets.Clear();
    }

    // ── Inner types ───────────────────────────────────────────────────────────

    private sealed class SubscriptionBucket
    {
        private readonly object                  _lock     = new();
        private readonly List<HandlerEntry>      _entries  = [];

        public int Count
        {
            get { lock (_lock) { return _entries.Count; } }
        }

        public EventSubscriptionToken Add(Delegate handler, EventPriority priority)
        {
            EventSubscriptionToken? token = null;
            token = new EventSubscriptionToken(handler.GetType(), () => Remove(token!));

            var entry = new HandlerEntry(handler, priority, token.SubscriptionId);

            lock (_lock)
            {
                _entries.Add(entry);
                // Keep sorted descending by priority so Sequential dispatch runs
                // Critical → High → Normal → Low without extra overhead at invoke time.
                _entries.Sort(static (a, b) => b.Priority.CompareTo(a.Priority));
            }

            return token;
        }

        public void Remove(EventSubscriptionToken token)
        {
            lock (_lock)
            {
                _entries.RemoveAll(e => e.SubscriptionId == token.SubscriptionId);
            }
        }

        public HandlerEntry[] Snapshot()
        {
            lock (_lock) { return [.. _entries]; }
        }
    }

    private sealed record HandlerEntry(
        Delegate      Handler,
        EventPriority Priority,
        Guid          SubscriptionId);
}
