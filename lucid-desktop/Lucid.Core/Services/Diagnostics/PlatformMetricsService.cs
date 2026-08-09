using System.Collections.Concurrent;

namespace Lucid.Services.Diagnostics;

/// <summary>
/// Production implementation of <see cref="IPlatformMetricsService"/>.
///
/// Uses Interlocked operations for counter increments to avoid lock contention
/// on hot reporting paths. Snapshot construction is infrequent and uses a
/// light lock for reading the rolling-window queues.
///
/// Rolling windows:
/// - Events per minute: sliding 60-second window via a timestamp queue.
/// - Exceptions per minute: same pattern.
/// - Average action latency: bounded ring buffer of last 100 latency samples.
/// </summary>
public sealed class PlatformMetricsService : IPlatformMetricsService, IDisposable
{
    private readonly PlatformMetricsCounters _counters = new();

    // Rolling window state — protected by _windowLock.
    private readonly object           _windowLock        = new();
    private readonly Queue<DateTime>  _eventTimestamps   = new();
    private readonly Queue<DateTime>  _exceptionTimestamps = new();
    private readonly Queue<double>    _latencySamples    = new();
    private          bool             _degradedModeActive;
    private          bool             _disposed;

    private const int LatencySampleSize = 100;

    // ── IPlatformMetricsService ───────────────────────────────────────────────

    /// <inheritdoc />
    public PlatformMetricsSnapshot GetSnapshot()
    {
        long eventsPerMinute;
        long exceptionsPerMinute;
        double avgLatency;
        int gcCount = 0;

        lock (_windowLock)
        {
            var cutoff = DateTime.UtcNow.AddSeconds(-60);
            eventsPerMinute     = PurgeAndCount(_eventTimestamps,     cutoff);
            exceptionsPerMinute = PurgeAndCount(_exceptionTimestamps, cutoff);
            avgLatency          = _latencySamples.Count > 0
                ? _latencySamples.Average()
                : 0.0;

            for (int gen = 0; gen <= GC.MaxGeneration; gen++)
                gcCount += GC.CollectionCount(gen);
        }

        var totalActions  = Interlocked.Read(ref _counters.TotalActionsExecuted);
        var totalFailures = Interlocked.Read(ref _counters.TotalActionFailures);

        int healthScore = ComputeHealthScore(
            totalActions, totalFailures,
            exceptionsPerMinute,
            _counters.DegradedModeTransitions,
            _degradedModeActive);

        return new PlatformMetricsSnapshot
        {
            TotalEventsPublished    = Interlocked.Read(ref _counters.TotalEventsPublished),
            EventsPerMinute         = eventsPerMinute,
            TotalActionsExecuted    = totalActions,
            TotalActionFailures     = totalFailures,
            AverageActionLatencyMs  = Math.Round(avgLatency, 1),
            TotalWorkflowsStarted   = Interlocked.Read(ref _counters.TotalWorkflowsStarted),
            TotalWorkflowsCompleted = Interlocked.Read(ref _counters.TotalWorkflowsCompleted),
            TotalExceptionsCaught   = Interlocked.Read(ref _counters.TotalExceptionsCaught),
            ExceptionsPerMinute     = exceptionsPerMinute,
            ManagedHeapBytes        = GC.GetTotalMemory(forceFullCollection: false),
            GcCollectionCount       = gcCount,
            DegradedModeTransitions = _counters.DegradedModeTransitions,
            IsDegradedModeActive    = _degradedModeActive,
            PlatformHealthScore     = healthScore,
        };
    }

    /// <inheritdoc />
    public void RecordEventPublished()
    {
        Interlocked.Increment(ref _counters.TotalEventsPublished);
        lock (_windowLock) { _eventTimestamps.Enqueue(DateTime.UtcNow); }
    }

    /// <inheritdoc />
    public void RecordActionStarted() =>
        Interlocked.Increment(ref _counters.TotalActionsExecuted);

    /// <inheritdoc />
    public void RecordActionCompleted(bool succeeded, double latencyMs)
    {
        if (!succeeded)
            Interlocked.Increment(ref _counters.TotalActionFailures);

        lock (_windowLock)
        {
            _latencySamples.Enqueue(latencyMs);
            while (_latencySamples.Count > LatencySampleSize)
                _latencySamples.Dequeue();
        }
    }

    /// <inheritdoc />
    public void RecordWorkflowStarted() =>
        Interlocked.Increment(ref _counters.TotalWorkflowsStarted);

    /// <inheritdoc />
    public void RecordWorkflowCompleted() =>
        Interlocked.Increment(ref _counters.TotalWorkflowsCompleted);

    /// <inheritdoc />
    public void RecordExceptionCaught()
    {
        Interlocked.Increment(ref _counters.TotalExceptionsCaught);
        lock (_windowLock) { _exceptionTimestamps.Enqueue(DateTime.UtcNow); }
    }

    /// <inheritdoc />
    public void RecordDegradedModeEntry()
    {
        Interlocked.Increment(ref _counters.DegradedModeTransitions);
        lock (_windowLock) { _degradedModeActive = true; }
    }

    /// <inheritdoc />
    public void RecordDegradedModeExit()
    {
        lock (_windowLock) { _degradedModeActive = false; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static long PurgeAndCount(Queue<DateTime> queue, DateTime cutoff)
    {
        while (queue.Count > 0 && queue.Peek() < cutoff)
            queue.Dequeue();
        return queue.Count;
    }

    private static int ComputeHealthScore(
        long totalActions,
        long totalFailures,
        long exceptionsPerMinute,
        int  degradedTransitions,
        bool isDegraded)
    {
        int score = 100;

        // Deduct for action failure rate.
        if (totalActions > 0)
        {
            double failureRate = (double)totalFailures / totalActions;
            score -= (int)(failureRate * 40); // up to -40 for 100% failure rate
        }

        // Deduct for exception rate.
        score -= (int)Math.Min(exceptionsPerMinute * 5, 30); // up to -30

        // Deduct for degraded mode.
        if (isDegraded) score -= 20;

        return Math.Clamp(score, 0, 100);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
