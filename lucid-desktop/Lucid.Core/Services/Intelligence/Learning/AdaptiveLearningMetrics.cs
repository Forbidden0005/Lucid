using System.Threading;

namespace Lucid.Services.Intelligence.Learning;

/// <summary>
/// Thread-safe runtime metrics for the adaptive learning pipeline.
///
/// Tracks the operational performance of Phase 21 learning systems to allow
/// Lucid to evaluate whether adaptive intelligence is improving accuracy.
///
/// Exposed via <c>AdaptiveLearningMetrics</c> on the composition root.
///
/// All counters use <see cref="Interlocked"/> — safe to read/write from any thread.
/// Privacy guarantee: counts and rates only — no identifiable content.
/// </summary>
public sealed class AdaptiveLearningMetrics
{
    private long   _patternsRecognized;
    private long   _baseLinesUpdated;
    private long   _effectivenessUpdates;
    private long   _calibrationAdjustments;
    private long   _driftAlertsRecorded;
    private long   _governanceSuppressedLearning;
    private long   _totalLearningCycles;
    private double _runningPatternConfidenceSum;
    private long   _patternConfidenceCount;

    // ── Update API ────────────────────────────────────────────────────────────

    public void RecordPatternRecognized(double confidence)
    {
        Interlocked.Increment(ref _patternsRecognized);
        Interlocked.Increment(ref _patternConfidenceCount);
        // Lock-free running sum — slight inaccuracy under contention is acceptable.
        _runningPatternConfidenceSum += confidence;
    }

    public void RecordBaselineUpdated()      => Interlocked.Increment(ref _baseLinesUpdated);
    public void RecordEffectivenessUpdated() => Interlocked.Increment(ref _effectivenessUpdates);
    public void RecordCalibrationAdjusted()  => Interlocked.Increment(ref _calibrationAdjustments);
    public void RecordDriftAlert()           => Interlocked.Increment(ref _driftAlertsRecorded);
    public void RecordLearningCycle()        => Interlocked.Increment(ref _totalLearningCycles);

    public void RecordGovernanceSuppressed() =>
        Interlocked.Increment(ref _governanceSuppressedLearning);

    // ── Read API ──────────────────────────────────────────────────────────────

    public long TotalPatternsRecognized     => Interlocked.Read(ref _patternsRecognized);
    public long TotalBaselinesUpdated       => Interlocked.Read(ref _baseLinesUpdated);
    public long TotalEffectivenessUpdates   => Interlocked.Read(ref _effectivenessUpdates);
    public long TotalCalibrationAdjustments => Interlocked.Read(ref _calibrationAdjustments);
    public long TotalDriftAlerts            => Interlocked.Read(ref _driftAlertsRecorded);
    public long TotalLearningCycles         => Interlocked.Read(ref _totalLearningCycles);
    public long TotalGovernanceSuppressed   => Interlocked.Read(ref _governanceSuppressedLearning);

    /// <summary>Average confidence of recognized patterns (0–1).</summary>
    public double AveragePatternConfidence
    {
        get
        {
            var count = Interlocked.Read(ref _patternConfidenceCount);
            return count == 0 ? 0 : _runningPatternConfidenceSum / count;
        }
    }

    /// <summary>
    /// Governance suppression rate — fraction of learning events constrained
    /// by governance policy. High rate indicates restrictive consent settings.
    /// </summary>
    public double GovernanceSuppressionRate
    {
        get
        {
            var total = TotalLearningCycles;
            return total == 0 ? 0 : (double)TotalGovernanceSuppressed / total;
        }
    }
}
