using System.Threading;

namespace Lucid.Services.Persistence.Reliability;

/// <summary>
/// Thread-safe counters for SQLite write-queue health monitoring.
///
/// Updated by <see cref="Lucid.Services.Persistence.SQLitePersistenceService"/>
/// and read by <see cref="PersistenceHealthMonitor"/> and the diagnostics page.
/// All operations use <see cref="Interlocked"/> — safe to read from any thread.
/// </summary>
public sealed class WriteQueueMetrics
{
    private long _droppedWriteCount;
    private long _totalWriteCount;
    private long _totalFlushCount;
    private long _lastDroppedAtTicks = -1L;

    // ── Mutation (called by SQLitePersistenceService) ─────────────────────────

    /// <summary>Records one dropped write. Returns the new cumulative count.</summary>
    public long RecordDrop()
    {
        Interlocked.Exchange(ref _lastDroppedAtTicks, DateTime.UtcNow.Ticks);
        return Interlocked.Increment(ref _droppedWriteCount);
    }

    /// <summary>Records one successfully enqueued write.</summary>
    public void RecordEnqueue() => Interlocked.Increment(ref _totalWriteCount);

    /// <summary>Records one completed flush batch.</summary>
    public void RecordFlush() => Interlocked.Increment(ref _totalFlushCount);

    // ── Observation ───────────────────────────────────────────────────────────

    /// <summary>Total writes dropped due to queue overflow since service start.</summary>
    public long DroppedWriteCount => Interlocked.Read(ref _droppedWriteCount);

    /// <summary>Total writes successfully enqueued since service start.</summary>
    public long TotalWriteCount => Interlocked.Read(ref _totalWriteCount);

    /// <summary>Total flush batches completed since service start.</summary>
    public long TotalFlushCount => Interlocked.Read(ref _totalFlushCount);

    /// <summary>UTC time of the most recent dropped write, or null if none have occurred.</summary>
    public DateTime? LastDroppedAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastDroppedAtTicks);
            return ticks == -1L ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    /// <summary>Drop rate as a fraction of total enqueued writes (0.0 – 1.0).</summary>
    public double DropRate
    {
        get
        {
            var total = Interlocked.Read(ref _totalWriteCount);
            return total == 0 ? 0.0 : Interlocked.Read(ref _droppedWriteCount) / (double)total;
        }
    }
}
