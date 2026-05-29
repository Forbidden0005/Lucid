using System.Threading;

namespace Lucid.Services.Reasoning.Cognitive;

/// <summary>
/// Thread-safe runtime metrics for the cognitive reasoning pipeline.
///
/// Exposed via <see cref="AppServices.CognitiveMetrics"/> and consumed by:
///   - DiagnosticsPageViewModel — shows reasoning health at a glance
///   - PlatformMetricsService — aggregates cross-service health
///   - ReasoningDiagnosticsService — provides context for trace records
///
/// All counters use <see cref="Interlocked"/> — safe to read/write from any thread.
/// </summary>
public sealed class CognitiveHealthMetrics
{
    private long   _totalCycles;
    private long   _totalInferences;
    private long   _totalSuppressedInferences;
    private long   _totalArbitrationPassed;
    private long   _totalArbitrationSuppressed;
    private long   _conflictingInferenceCount;
    private double _runningConfidenceSum;
    private double _runningCycleDurationMsSum;
    private long   _lastCycleTicks = -1L;

    // ── Update API (called by CognitiveReasoningEngine + RecommendationArbitrator) ──

    /// <summary>Records completion of one reasoning cycle.</summary>
    public void RecordCycle(
        int    inferenceCount,
        int    suppressedCount,
        double maxConfidence,
        double durationMs)
    {
        Interlocked.Increment(ref _totalCycles);
        Interlocked.Add(ref _totalInferences, inferenceCount);
        Interlocked.Add(ref _totalSuppressedInferences, suppressedCount);
        Interlocked.Exchange(ref _lastCycleTicks, DateTime.UtcNow.Ticks);

        // Approximate running averages (lock-free, eventually consistent).
        // These are display-only — slight inaccuracy under contention is acceptable.
        _runningConfidenceSum      += maxConfidence;
        _runningCycleDurationMsSum += durationMs;
    }

    /// <summary>Records one arbitration pass decision.</summary>
    public void RecordArbitrationPassed()  => Interlocked.Increment(ref _totalArbitrationPassed);

    /// <summary>Records one arbitration suppress decision.</summary>
    public void RecordArbitrationSuppressed() => Interlocked.Increment(ref _totalArbitrationSuppressed);

    /// <summary>Records a conflicting inference pair (two inferences that contradict).</summary>
    public void RecordConflictingInference() => Interlocked.Increment(ref _conflictingInferenceCount);

    // ── Read API ──────────────────────────────────────────────────────────────

    /// <summary>Total reasoning cycles run since service start.</summary>
    public long TotalCycles => Interlocked.Read(ref _totalCycles);

    /// <summary>Total inferences produced since service start (including suppressed).</summary>
    public long TotalInferences => Interlocked.Read(ref _totalInferences);

    /// <summary>Total inferences suppressed by context since service start.</summary>
    public long TotalSuppressedInferences => Interlocked.Read(ref _totalSuppressedInferences);

    /// <summary>Total arbitration decisions where an action was surfaced.</summary>
    public long ArbitrationPassed => Interlocked.Read(ref _totalArbitrationPassed);

    /// <summary>Total arbitration decisions where an action was suppressed.</summary>
    public long ArbitrationSuppressed => Interlocked.Read(ref _totalArbitrationSuppressed);

    /// <summary>Total conflicting inference pairs detected.</summary>
    public long ConflictingInferenceCount => Interlocked.Read(ref _conflictingInferenceCount);

    /// <summary>UTC time of the most recent completed cycle. Null before first cycle.</summary>
    public DateTime? LastCycleAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastCycleTicks);
            return ticks == -1L ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    /// <summary>
    /// Approximate average inference confidence (0–1) across all cycles.
    /// Based on the max confidence per cycle.
    /// </summary>
    public double AverageConfidence
    {
        get
        {
            var cycles = Interlocked.Read(ref _totalCycles);
            return cycles == 0 ? 0 : _runningConfidenceSum / cycles;
        }
    }

    /// <summary>Approximate average cycle duration in milliseconds.</summary>
    public double AverageCycleDurationMs
    {
        get
        {
            var cycles = Interlocked.Read(ref _totalCycles);
            return cycles == 0 ? 0 : _runningCycleDurationMsSum / cycles;
        }
    }

    /// <summary>
    /// Arbitration suppression rate (0.0–1.0).
    /// Proportion of evaluated actions that were suppressed rather than surfaced.
    /// </summary>
    public double ArbitrationSuppressionRate
    {
        get
        {
            var total = Interlocked.Read(ref _totalArbitrationPassed) +
                        Interlocked.Read(ref _totalArbitrationSuppressed);
            return total == 0 ? 0 : (double)Interlocked.Read(ref _totalArbitrationSuppressed) / total;
        }
    }

    /// <summary>
    /// Context-suppression rate — fraction of inferences suppressed by context.
    /// A high rate indicates the context engine is correctly filtering noise.
    /// </summary>
    public double InferenceSuppressionRate
    {
        get
        {
            var total = Interlocked.Read(ref _totalInferences);
            return total == 0 ? 0 : (double)Interlocked.Read(ref _totalSuppressedInferences) / total;
        }
    }
}
