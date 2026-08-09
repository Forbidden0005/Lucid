using Lucid.Services.Reasoning.Cognitive;

namespace Lucid.Services.Reasoning.Memory;

/// <summary>
/// Analyzes whether a new inference is consistent with historical patterns.
///
/// Consistency analysis answers:
///   - Has this type of inference appeared before? How frequently?
///   - Is confidence trending up (worsening) or down (improving)?
///   - Does this pattern correlate with a specific workload context?
///
/// Results are used to:
///   1. Boost confidence when a pattern is consistently recurring.
///   2. Reduce confidence when a pattern has high variability.
///   3. Annotate inferences with historical context ("seen 5 times this week").
///
/// This is read-only analysis — it never modifies the memory store.
/// Stateless — instantiate once and call Analyze() as needed.
/// </summary>
public sealed class PatternConsistencyAnalyzer
{
    private const int MaxHistoryWindow = 100; // scan last N entries

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Analyzes the given inference against a window of historical records.
    /// Returns a <see cref="PatternConsistencyResult"/> describing what was found.
    /// </summary>
    public PatternConsistencyResult Analyze(
        OperationalInference              inference,
        IReadOnlyList<HistoricalInferenceRecord> history)
    {
        if (history.Count == 0)
            return PatternConsistencyResult.NoHistory(inference.InferenceId);

        // Derive the type for the current inference so we can group against history.
        var inferenceType = HistoricalInferenceRecord.FromInference(inference).InferenceType;

        // Scan the most recent window and find type-matching entries.
        var recentEntries = history
            .TakeLast(MaxHistoryWindow)
            .Where(e => string.Equals(e.InferenceType, inferenceType,
                                      StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.RecordedAtUtc)
            .ToList();

        if (recentEntries.Count == 0)
            return PatternConsistencyResult.NoHistory(inference.InferenceId);

        // Frequency: how many times in the last 7 days?
        var weekAgo     = DateTime.UtcNow - TimeSpan.FromDays(7);
        var recentCount = recentEntries.Count(e => e.RecordedAtUtc >= weekAgo);

        // Confidence trend: current vs historical average (ConfidenceLevel → double).
        var avgHistoricalConfidence = recentEntries
            .Average(e => ConfidenceLevelToScore(e.ConfidenceAtTime));
        var currentConfidence = inference.Confidence.Value;
        var confidenceTrend   = currentConfidence - avgHistoricalConfidence;

        // Stability: are all entries the same priority, or fluctuating?
        var uniquePriorities = recentEntries.Select(e => e.PriorityAtTime).Distinct().Count();
        var isStable         = uniquePriorities <= 2;

        double consistencyBoost = ComputeConsistencyBoost(recentCount, isStable, confidenceTrend);

        return new PatternConsistencyResult(
            InferenceId:        inference.InferenceId,
            InferenceType:      inferenceType,
            PatternOccurrences: recentCount,
            AverageConfidence:  avgHistoricalConfidence,
            ConfidenceTrend:    confidenceTrend,
            IsStablePattern:    isStable,
            ConsistencyBoost:   consistencyBoost,
            LastSeenAtUtc:      recentEntries.First().RecordedAtUtc,
            AnalyzedAtUtc:      DateTime.UtcNow);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>Maps a ConfidenceLevel enum to a representative double score (0–1).</summary>
    private static double ConfidenceLevelToScore(ConfidenceLevel level) => level switch
    {
        ConfidenceLevel.Certain     => 0.95,
        ConfidenceLevel.High        => 0.75,
        ConfidenceLevel.Medium      => 0.55,
        ConfidenceLevel.Low         => 0.35,
        _                           => 0.15,   // Speculative
    };

    /// <summary>
    /// Computes a consistency boost value in [-0.15, +0.15].
    /// Positive = well-established recurring pattern, negative = noisy/unstable.
    /// </summary>
    private static double ComputeConsistencyBoost(
        int    recentCount,
        bool   isStable,
        double confidenceTrend)
    {
        double boost = 0;

        // Frequent recurring pattern (≥ 3 times this week) adds weight.
        if (recentCount >= 5)       boost += 0.10;
        else if (recentCount >= 3)  boost += 0.05;
        else if (recentCount == 1)  boost -= 0.05; // seen once — may be noise

        // Stable priority pattern (not fluctuating between severity levels) adds trust.
        if (isStable) boost += 0.05;
        else          boost -= 0.05;

        // Rising confidence trend (worsening signal) increases priority weight.
        if (confidenceTrend > 0.10)       boost += 0.05;
        else if (confidenceTrend < -0.15) boost -= 0.05; // improving — lower urgency

        return Math.Clamp(boost, -0.15, 0.15);
    }
}

/// <summary>
/// Result of a pattern consistency analysis for a single inference.
/// </summary>
public sealed record PatternConsistencyResult(
    Guid     InferenceId,
    string   InferenceType,
    int      PatternOccurrences,
    double   AverageConfidence,
    double   ConfidenceTrend,
    bool     IsStablePattern,
    double   ConsistencyBoost,
    DateTime LastSeenAtUtc,
    DateTime AnalyzedAtUtc)
{
    /// <summary>True when there was no historical data to analyze.</summary>
    public bool HasNoHistory => PatternOccurrences == 0;

    /// <summary>Human-readable description of the pattern finding.</summary>
    public string PatternSummary => PatternOccurrences switch
    {
        0     => "No historical data for this pattern.",
        1     => "Observed once previously — may be transient.",
        2     => "Observed twice previously — pattern establishing.",
        >= 5  => $"Recurring pattern — observed {PatternOccurrences} times this week.",
        _     => $"Observed {PatternOccurrences} times previously.",
    };

    /// <summary>Creates a result representing no historical data.</summary>
    public static PatternConsistencyResult NoHistory(Guid inferenceId) =>
        new(inferenceId, "Unknown", 0, 0, 0, true, 0, DateTime.MinValue, DateTime.UtcNow);
}
