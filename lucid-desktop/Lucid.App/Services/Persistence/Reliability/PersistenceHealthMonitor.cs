using Lucid.Services.Infrastructure.Events;
using System.Threading;

namespace Lucid.Services.Persistence.Reliability;

/// <summary>
/// Monitors <see cref="WriteQueueMetrics"/> and publishes
/// <see cref="PersistenceQueueOverflowEvent"/> events when persistence health
/// degrades beyond configured thresholds.
///
/// Wired into <see cref="Lucid.Services.Persistence.SQLitePersistenceService"/>
/// at startup via <see cref="Lucid.AppServices"/>.
///
/// Thresholds:
///   • Any dropped write triggers an event (first-drop is always noteworthy).
///   • Subsequent events are rate-limited to one per <see cref="EventThrottleWindow"/>
///     to avoid flooding the event bus.
/// </summary>
public sealed class PersistenceHealthMonitor
{
    private static readonly TimeSpan EventThrottleWindow = TimeSpan.FromSeconds(60);

    private readonly WriteQueueMetrics _metrics;
    private readonly IEventBus         _eventBus;
    private          long              _lastEventTicks = -1L;

    public PersistenceHealthMonitor(WriteQueueMetrics metrics, IEventBus eventBus)
    {
        _metrics  = metrics;
        _eventBus = eventBus;
    }

    /// <summary>
    /// Called by SQLitePersistenceService immediately after recording a drop.
    /// Publishes an event if the throttle window has elapsed.
    /// </summary>
    public void OnWriteDropped(int currentQueueDepth, int maxQueueDepth)
    {
        var now = DateTime.UtcNow;
        var lastTicks = Interlocked.Read(ref _lastEventTicks);

        if (lastTicks != -1L)
        {
            var elapsed = now - new DateTime(lastTicks, DateTimeKind.Utc);
            if (elapsed < EventThrottleWindow) return;
        }

        Interlocked.Exchange(ref _lastEventTicks, now.Ticks);

        _ = _eventBus.PublishAsync(new PersistenceQueueOverflowEvent(
            DroppedCount:  _metrics.DroppedWriteCount,
            QueueDepth:    currentQueueDepth,
            MaxQueueDepth: maxQueueDepth)
        {
            Source   = "Persistence",
            Priority = EventPriority.Low,
        });
    }

    /// <summary>
    /// Called by SQLitePersistenceService when a direct write fails.
    /// Always publishes (direct writes are important — failures are not silenced).
    /// </summary>
    public void OnDirectWriteFailed(string context, string error)
    {
        _ = _eventBus.PublishAsync(new PersistenceWriteFailedEvent(
            OperationContext: context,
            ErrorMessage:     error)
        {
            Source   = "Persistence",
            Priority = EventPriority.Normal,
        });
    }

    /// <summary>Current metrics snapshot for diagnostics consumption.</summary>
    public WriteQueueMetrics Metrics => _metrics;
}
