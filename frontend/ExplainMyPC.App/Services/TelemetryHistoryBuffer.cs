using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Telemetry;

namespace ExplainMyPC.Services;

/// <summary>
/// Circular-buffer implementation of <see cref="ITelemetryHistoryBuffer"/>.
///
/// Storage layout:
///   Five parallel arrays of equal length (timestamps + one per metric).
///   All arrays are allocated once at construction — no per-sample heap
///   allocations on the write path.
///
///   At 1.5 s per poll tick the default capacity of 1 200 covers exactly
///   30 minutes. A small timing margin means the oldest sample in a
///   LastThirtyMinutes query will always exist even if polls run slightly fast.
///
/// Memory:
///   5 arrays × 1 200 entries × 8 bytes = ~48 KB — negligible.
///
/// Ring-buffer semantics:
///   _head  — index of the oldest valid entry.
///   _count — number of valid entries (0 … _capacity).
///   New entries write to (_head + _count) % _capacity.
///   When the buffer is full, _head advances to evict the oldest entry.
///
/// Concurrency model:
///   Record() holds an exclusive write lock for the duration of a single
///   array-slot update (≈5 assignments, nanosecond range).
///   GetSamples() and GetStats() hold a shared read lock, allowing multiple
///   background analysis threads to query simultaneously.
/// </summary>
public sealed class TelemetryHistoryBuffer : ITelemetryHistoryBuffer, IDisposable
{
    // 30 min × 60 s ÷ 1.5 s per sample = 1 200. A small margin avoids
    // premature eviction when polls fire slightly ahead of schedule.
    private const int DefaultCapacity = 1_200;

    private readonly int _capacity;

    // Parallel arrays — position i in every array describes the same sample.
    private readonly DateTimeOffset[] _timestamps;
    private readonly double[]         _cpu;
    private readonly double[]         _ram;
    private readonly double[]         _gpu;
    private readonly double[]         _disk;

    // Ring-buffer state (guarded by _lock).
    private int _head;   // index of oldest valid entry
    private int _count;  // valid entries in [0, _capacity]

    private readonly ReaderWriterLockSlim _lock =
        new(LockRecursionPolicy.NoRecursion);

    private bool _disposed;

    // ── Public properties ─────────────────────────────────────────────────────

    public int Capacity => _capacity;

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try   { return _count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    // ── Construction ──────────────────────────────────────────────────────────

    public TelemetryHistoryBuffer(int capacity = DefaultCapacity)
    {
        _capacity   = capacity;
        _timestamps = new DateTimeOffset[capacity];
        _cpu        = new double[capacity];
        _ram        = new double[capacity];
        _gpu        = new double[capacity];
        _disk       = new double[capacity];
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends one snapshot to the ring. When the buffer is full, the oldest
    /// entry is silently replaced (standard circular-buffer eviction).
    /// </summary>
    public void Record(TelemetrySnapshot snapshot)
    {
        var now = DateTimeOffset.Now;

        _lock.EnterWriteLock();
        try
        {
            int writeIdx = (_head + _count) % _capacity;

            _timestamps[writeIdx] = now;
            _cpu [writeIdx]       = snapshot.CpuPercent;
            _ram [writeIdx]       = snapshot.RamPercent;
            _gpu [writeIdx]       = snapshot.GpuPercent;
            _disk[writeIdx]       = snapshot.DiskPercent;

            if (_count < _capacity)
                _count++;
            else
                _head = (_head + 1) % _capacity; // evict oldest
        }
        finally { _lock.ExitWriteLock(); }
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<MetricPoint> GetSamples(TelemetryMetric metric, TimeWindow window)
    {
        var    cutoff = DateTimeOffset.Now - TimeSpan.FromSeconds((int)window);
        double[] src  = SelectArray(metric);

        _lock.EnterReadLock();
        try
        {
            var result = new List<MetricPoint>();

            for (int i = 0; i < _count; i++)
            {
                int idx = (_head + i) % _capacity;
                if (_timestamps[idx] >= cutoff)
                    result.Add(new MetricPoint(_timestamps[idx], src[idx]));
            }

            return result;
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <inheritdoc/>
    public MetricStats GetStats(TelemetryMetric metric, TimeWindow window)
    {
        var    cutoff = DateTimeOffset.Now - TimeSpan.FromSeconds((int)window);
        double[] src  = SelectArray(metric);

        _lock.EnterReadLock();
        try
        {
            double min    = double.MaxValue;
            double max    = double.MinValue;
            double sum    = 0;
            double latest = 0;
            int    count  = 0;

            for (int i = 0; i < _count; i++)
            {
                int idx = (_head + i) % _capacity;
                if (_timestamps[idx] < cutoff) continue;

                double v = src[idx];
                if (v < min) min = v;
                if (v > max) max = v;
                sum    += v;
                latest  = v;
                count++;
            }

            if (count == 0) return new MetricStats(0, 0, 0, 0, 0);

            return new MetricStats(
                Min:         Math.Round(min,         1),
                Max:         Math.Round(max,         1),
                Average:     Math.Round(sum / count,  1),
                Latest:      latest,
                SampleCount: count);
        }
        finally { _lock.ExitReadLock(); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private double[] SelectArray(TelemetryMetric metric) => metric switch
    {
        TelemetryMetric.Cpu  => _cpu,
        TelemetryMetric.Ram  => _ram,
        TelemetryMetric.Gpu  => _gpu,
        TelemetryMetric.Disk => _disk,
        _ => throw new ArgumentOutOfRangeException(nameof(metric), $"Unknown metric: {metric}")
    };

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.Dispose();
    }
}
