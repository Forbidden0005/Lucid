using Lucid.Services.Reasoning.Cognitive;
using Lucid.Services.Reasoning.Memory;

namespace Lucid.Services.Intelligence.Patterns;

/// <summary>
/// Analyzes historical inference records to produce a list of recognized
/// recurring <see cref="OperationalPattern"/> instances.
///
/// The engine processes a window of <see cref="HistoricalInferenceRecord"/> entries
/// and groups them by inference type, computing occurrence counts, confidence,
/// and workload associations to build meaningful multi-session pattern records.
///
/// Integration:
///   - Input: <see cref="IReasoningMemoryService.GetRecent"/> history window
///   - Output: <see cref="IReadOnlyList{OperationalPattern}"/> sorted by confidence
///   - Side effect: updates <see cref="RecurrenceTracker"/> with each inference type
///
/// Transparency guarantee: every OperationalPattern is fully derivable from
/// the input history — no hidden state, no black-box weighting.
///
/// Thread-safe: stateless analysis methods; RecurrenceTracker is thread-safe.
/// </summary>
public sealed class PatternIntelligenceEngine
{
    private readonly RecurrenceTracker _tracker;

    /// <summary>Minimum occurrences before a group becomes an OperationalPattern.</summary>
    private const int MinOccurrences = 2;

    /// <summary>Maximum observation window to analyze.</summary>
    private const int MaxHistoryEntries = 200;

    public PatternIntelligenceEngine(RecurrenceTracker? tracker = null)
    {
        _tracker = tracker ?? new RecurrenceTracker();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Analyzes the provided history window and returns detected patterns,
    /// sorted by confidence descending.
    /// </summary>
    public IReadOnlyList<OperationalPattern> Analyze(
        IReadOnlyList<HistoricalInferenceRecord> history)
    {
        if (history.Count == 0) return [];

        var window = history.TakeLast(MaxHistoryEntries).ToList();

        // Group by InferenceType to find recurrences.
        var groups = window
            .GroupBy(r => r.InferenceType, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= MinOccurrences)
            .ToList();

        var patterns = new List<OperationalPattern>(groups.Count);

        foreach (var group in groups)
        {
            var entries      = group.OrderBy(r => r.RecordedAtUtc).ToList();
            var patternKey   = BuildPatternKey(group.Key, entries);
            var confidence   = ComputePatternConfidence(entries);
            var isStable     = IsStablePattern(entries);
            var workload     = DominantWorkloadContext(entries);

            // Update the recurrence tracker.
            _tracker.Record(patternKey, workload);

            var allDomains = entries
                .SelectMany(e => e.Domains)
                .GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .Take(4)
                .ToList()
                .AsReadOnly();

            patterns.Add(new OperationalPattern
            {
                PatternKey              = patternKey,
                PatternType             = group.Key,
                Description             = BuildDescription(group.Key, entries.Count, workload),
                OccurrenceCount         = entries.Count,
                Domains                 = allDomains,
                PatternConfidence       = confidence,
                FirstSeenAt             = entries.First().RecordedAtUtc,
                LastSeenAt              = entries.Last().RecordedAtUtc,
                DominantWorkloadContext = workload,
                IsStable                = isStable,
            });
        }

        return patterns
            .OrderByDescending(p => p.PatternConfidence)
            .ThenByDescending(p => p.OccurrenceCount)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>Exposes the underlying recurrence tracker for diagnostics.</summary>
    public RecurrenceTracker Tracker => _tracker;

    // ── Private helpers ────────────────────────────────────────────────────────

    private static string BuildPatternKey(string inferenceType, List<HistoricalInferenceRecord> entries)
    {
        // Key = type + dominant domain set (stable across instances).
        var topDomain = entries
            .SelectMany(e => e.Domains)
            .GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key ?? "General";

        return $"{inferenceType}:{topDomain}".ToLowerInvariant();
    }

    private static double ComputePatternConfidence(List<HistoricalInferenceRecord> entries)
    {
        // Base: fraction of entries with High/Certain confidence.
        var highConfidenceCount = entries.Count(e =>
            e.ConfidenceAtTime >= ConfidenceLevel.High);

        double baseScore = (double)highConfidenceCount / entries.Count;

        // Boost for recurrence count (diminishing returns).
        double recurrenceBoost = entries.Count switch
        {
            >= 10 => 0.20,
            >= 5  => 0.15,
            >= 3  => 0.10,
            _     => 0.00,
        };

        // Penalty for suppressed inferences (may indicate false signals).
        var suppressedFraction = (double)entries.Count(e => e.WasSuppressed) / entries.Count;
        double suppressionPenalty = suppressedFraction * 0.30;

        return Math.Clamp(baseScore + recurrenceBoost - suppressionPenalty, 0.0, 0.95);
    }

    private static bool IsStablePattern(List<HistoricalInferenceRecord> entries)
    {
        if (entries.Count < 2) return false;

        // Stable = priority levels don't fluctuate wildly.
        var uniquePriorities = entries.Select(e => e.PriorityAtTime).Distinct().Count();
        return uniquePriorities <= 2;
    }

    private static string? DominantWorkloadContext(List<HistoricalInferenceRecord> entries)
    {
        var contexts = entries
            .Where(e => !string.IsNullOrEmpty(e.WorkloadContext))
            .GroupBy(e => e.WorkloadContext!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (contexts.Count == 0) return null;

        // Only claim a dominant context if it accounts for > 50% of entries.
        var top = contexts.First();
        return (double)top.Count() / entries.Count > 0.5 ? top.Key : null;
    }

    private static string BuildDescription(string inferenceType, int count, string? workload)
    {
        var base_ = inferenceType switch
        {
            "StartupCongestion"  => "Startup resource pressure",
            "ThermalStress"      => "Thermal elevation",
            "StoragePressure"    => "Storage space pressure",
            "CombinedPressure"   => "Combined multi-domain resource pressure",
            "SessionDegradation" => "Session-length performance degradation",
            "SustainedPressure"  => "Sustained resource pressure",
            _                    => inferenceType,
        };

        var countStr = count switch
        {
            2     => "has occurred twice",
            3     => "has occurred 3 times",
            >= 10 => $"recurs regularly ({count} occurrences)",
            _     => $"has occurred {count} times",
        };

        return workload is not null
            ? $"{base_} {countStr}, often during {workload}."
            : $"{base_} {countStr} in this session window.";
    }
}
