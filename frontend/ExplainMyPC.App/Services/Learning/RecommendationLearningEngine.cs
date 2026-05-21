namespace ExplainMyPC.Services.Learning;

/// <summary>
/// Aggregates a flat list of <see cref="ActionOutcomeRecord"/> instances into a
/// per-action <see cref="RecommendationEffectivenessProfile"/> dictionary.
///
/// This class is stateless — BuildProfiles() is a pure function.
/// It is called by <see cref="RemediationLearningService"/> after each analysis pass.
///
/// Effectiveness labelling:
///   • &lt; 2 analyzable records  → "Limited historical data"
///   • Rate ≥ 0.70              → "Historically effective on this machine"
///   • Rate ≥ 0.30              → "Mixed results on this machine"
///   • Rate &lt; 0.30             → "Low effectiveness on this machine"
///
/// Rate formula:
///   rate = (ImprovedCount * 1.0 + PartiallyImprovedCount * 0.5) / TotalAnalyzed
///
/// Where TotalAnalyzed = TotalExecutions − InsufficientDataCount.
/// </summary>
internal static class RecommendationLearningEngine
{
    // ── Thresholds ─────────────────────────────────────────────────────────────

    private const int    MinWarmRecords   = 2;    // minimum analyses before the profile is "warm"
    private const double HighEffThreshold = 0.70; // ≥70% → "Historically effective"
    private const double LowEffThreshold  = 0.30; // <30% → "Low effectiveness"

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a fresh dictionary of effectiveness profiles from all available
    /// outcome records.  Each unique ActionKey gets one profile.
    /// </summary>
    public static IReadOnlyDictionary<string, RecommendationEffectivenessProfile>
        BuildProfiles(IReadOnlyList<ActionOutcomeRecord> records)
    {
        // Group by action key
        var groups = records
            .GroupBy(r => r.ActionKey, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => BuildProfile(g.Key, g.ToList()),
                StringComparer.Ordinal);

        return groups;
    }

    // ── Per-group builder ──────────────────────────────────────────────────────

    private static RecommendationEffectivenessProfile BuildProfile(
        string                  actionKey,
        List<ActionOutcomeRecord> records)
    {
        // Sort by execution time — most recent last
        records.Sort((a, b) => a.ExecutedAt.CompareTo(b.ExecutedAt));

        // Outcome counts
        int improved        = records.Count(r => r.Outcome == OutcomeClassification.Improved);
        int partial         = records.Count(r => r.Outcome == OutcomeClassification.PartiallyImproved);
        int unchanged       = records.Count(r => r.Outcome == OutcomeClassification.Unchanged);
        int worsened        = records.Count(r => r.Outcome == OutcomeClassification.Worsened);
        int insufficient    = records.Count(r => r.Outcome == OutcomeClassification.InsufficientData);
        int totalAnalyzed   = records.Count - insufficient;
        int totalExecutions = records.Count;

        // Title from most recent record
        string title = records[^1].ActionTitle;

        // Effectiveness rate (cold-start guard)
        double rate = totalAnalyzed >= MinWarmRecords
            ? (improved * 1.0 + partial * 0.5) / totalAnalyzed
            : 0.0;

        bool isWarmEnough = totalAnalyzed >= MinWarmRecords;

        // Label
        string label = BuildLabel(rate, isWarmEnough);

        // Average metric deltas (over positive outcomes only)
        var positiveRecords = records
            .Where(r => r.Outcome is OutcomeClassification.Improved
                                  or OutcomeClassification.PartiallyImproved)
            .ToList();

        double avgCpu  = positiveRecords.Count > 0
            ? positiveRecords.Average(r => r.CpuAfter  - r.CpuBefore)  : 0;
        double avgRam  = positiveRecords.Count > 0
            ? positiveRecords.Average(r => r.RamAfter  - r.RamBefore)  : 0;
        double avgDisk = positiveRecords.Count > 0
            ? positiveRecords.Average(r => r.DiskAfter - r.DiskBefore) : 0;

        // Summary sentence
        string summary = BuildSummary(
            actionKey, title, rate, isWarmEnough, totalExecutions, totalAnalyzed,
            improved, partial, avgCpu, avgRam, avgDisk);

        return new RecommendationEffectivenessProfile
        {
            ActionKey             = actionKey,
            ActionTitle           = title,
            TotalExecutions       = totalExecutions,
            ImprovedCount         = improved,
            PartiallyImprovedCount = partial,
            UnchangedCount        = unchanged,
            WorsenedCount         = worsened,
            InsufficientDataCount = insufficient,
            EffectivenessRate     = rate,
            EffectivenessLabel    = label,
            SummaryText           = summary,
            IsWarmEnough          = isWarmEnough,
            AvgCpuChange          = avgCpu,
            AvgRamChange          = avgRam,
            AvgDiskChange         = avgDisk,
            LastAnalyzedAt        = records[^1].AnalyzedAt,
        };
    }

    // ── Label builder ──────────────────────────────────────────────────────────

    private static string BuildLabel(double rate, bool isWarmEnough)
    {
        if (!isWarmEnough)
            return "Limited historical data";

        return rate switch
        {
            >= HighEffThreshold => "Historically effective on this machine",
            >= LowEffThreshold  => "Mixed results on this machine",
            _                   => "Low effectiveness on this machine",
        };
    }

    // ── Summary sentence builder ───────────────────────────────────────────────

    private static string BuildSummary(
        string actionKey,
        string actionTitle,
        double rate,
        bool   isWarmEnough,
        int    totalExecutions,
        int    totalAnalyzed,
        int    improved,
        int    partial,
        double avgCpuChange,
        double avgRamChange,
        double avgDiskChange)
    {
        if (!isWarmEnough)
        {
            return totalAnalyzed == 0
                ? $"No outcome data available yet — run this action to start building effectiveness history."
                : $"Only {totalAnalyzed} {(totalAnalyzed == 1 ? "analysis" : "analyses")} available — needs at least {MinWarmRecords} for a reliable pattern.";
        }

        // Find the best improving metric signal
        string metricHint = BuildMetricHint(avgCpuChange, avgRamChange, avgDiskChange);

        int positiveCount = improved + partial;
        string triesText = $"{positiveCount} of {totalAnalyzed} {(totalAnalyzed == 1 ? "try" : "tries")}";

        if (rate >= HighEffThreshold)
        {
            return metricHint.Length > 0
                ? $"{metricHint} on {triesText}."
                : $"Showed measurable improvement on {triesText}.";
        }

        if (rate >= LowEffThreshold)
        {
            return metricHint.Length > 0
                ? $"Partial improvement observed ({triesText}). {metricHint}."
                : $"Showed mixed results — improved on {triesText}.";
        }

        return $"Did not produce consistent improvement ({positiveCount} of {totalAnalyzed} {(totalAnalyzed == 1 ? "try" : "tries")} showed benefit).";
    }

    private static string BuildMetricHint(double avgCpu, double avgRam, double avgDisk)
    {
        // avgXChange is negative when metrics improved (went down)
        // Pick the strongest single-metric signal
        double cpuDrop  = -avgCpu;
        double ramDrop  = -avgRam;
        double diskDrop = -avgDisk;

        double best = Math.Max(cpuDrop, Math.Max(ramDrop, diskDrop));
        if (best < 3.0) return ""; // no meaningful metric signal

        string metric = cpuDrop >= best  ? "CPU pressure"
                      : ramDrop >= best  ? "RAM pressure"
                      : "disk pressure";

        return $"Previously helped reduce {metric} by ~{best:F0}%";
    }
}
