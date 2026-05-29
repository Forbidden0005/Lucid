using System.Collections.Concurrent;

namespace Lucid.Services.Intelligence.Calibration;

/// <summary>
/// Tracks prediction accuracy per inference type over time.
///
/// For each inference type, maintains a rolling window of
/// <see cref="CalibrationRecord"/> entries and computes:
///   - Confirmation rate (how often predictions were correct)
///   - Mean calibration error (average over/under-confidence)
///   - Trend (is accuracy improving or declining?)
///
/// Thread-safe: ConcurrentDictionary + ConcurrentQueue per inference type.
/// In-memory only — calibration history is session-lifetime.
/// </summary>
public sealed class HistoricalAccuracyTracker
{
    private const int MaxRecordsPerType = 100;

    // Key: inferenceType → queue of calibration records
    private readonly ConcurrentDictionary<string, ConcurrentQueue<CalibrationRecord>>
        _records = new();

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Records a calibration outcome for the given inference type.</summary>
    public void Record(CalibrationRecord record)
    {
        var queue = _records.GetOrAdd(
            record.InferenceType,
            _ => new ConcurrentQueue<CalibrationRecord>());

        queue.Enqueue(record);

        while (queue.Count > MaxRecordsPerType)
            queue.TryDequeue(out _);
    }

    /// <summary>
    /// Returns an <see cref="AccuracySummary"/> for the given inference type.
    /// Returns null when no records exist for the type.
    /// </summary>
    public AccuracySummary? GetSummary(string inferenceType)
    {
        if (!_records.TryGetValue(inferenceType, out var queue))
            return null;

        var records = queue.ToArray();
        if (records.Length == 0) return null;

        var confirmed   = records.Count(r => r.WasConfirmed);
        var totalErrors = records.Sum(r => r.CalibrationError);
        var meanError   = totalErrors / records.Length;

        // Compute trend from the last half vs first half of records.
        var half    = records.Length / 2;
        double trend = 0;
        if (half >= 5)
        {
            var earlyError = records.Take(half).Average(r => r.CalibrationError);
            var lateError  = records.TakeLast(half).Average(r => r.CalibrationError);
            trend = lateError - earlyError; // negative = improving (less over-confident)
        }

        return new AccuracySummary(
            InferenceType:   inferenceType,
            RecordCount:     records.Length,
            ConfirmationRate: (double)confirmed / records.Length,
            MeanCalibrationError: meanError,
            CalibrationTrend: trend,
            IsReliable: records.Length >= 10);
    }

    /// <summary>Returns accuracy summaries for all tracked inference types.</summary>
    public IReadOnlyList<AccuracySummary> GetAllSummaries() =>
        _records.Keys
                .Select(GetSummary)
                .Where(s => s is not null)
                .Select(s => s!)
                .OrderByDescending(s => s.RecordCount)
                .ToList()
                .AsReadOnly();

    /// <summary>Total inference types being tracked.</summary>
    public int TrackedTypeCount => _records.Count;
}

/// <summary>
/// Aggregated accuracy summary for one inference type.
/// </summary>
public sealed record AccuracySummary(
    string InferenceType,
    int    RecordCount,
    double ConfirmationRate,
    double MeanCalibrationError,
    double CalibrationTrend,
    bool   IsReliable)
{
    /// <summary>True when the engine is systematically over-confident (mean error > 0.15).</summary>
    public bool IsOverConfident => MeanCalibrationError > 0.15;

    /// <summary>True when the engine is systematically under-confident (mean error &lt; -0.15).</summary>
    public bool IsUnderConfident => MeanCalibrationError < -0.15;

    /// <summary>True when calibration is improving (trend is declining toward zero).</summary>
    public bool IsImproving =>
        MeanCalibrationError > 0 ? CalibrationTrend < -0.02 :  // over-confident and improving
        MeanCalibrationError < 0 ? CalibrationTrend > 0.02  :  // under-confident and improving
        Math.Abs(CalibrationTrend) < 0.02;                      // well-calibrated and stable
}
