using Lucid.Services.Intelligence;

namespace Lucid.Services.Intelligence.Arbitration;

/// <summary>
/// Merges related recommendation clusters when multiple clusters address the
/// same root cause or represent complementary aspects of the same problem.
///
/// Merge scenarios:
///   • Performance + Thermal clusters where both stem from the same combined pressure
///     condition → merge into a single "System under combined stress" cluster.
///   • Storage + Repair clusters where storage pressure is causing system repair needs
///     → merge into a single "Storage health" cluster.
///   • Startup + Performance clusters during post-boot windows
///     → merge into "Startup optimization" cluster.
///
/// Merging is conservative — clusters are only merged when there is strong
/// evidence of relationship (shared insight IDs, overlapping domains,
/// correlated timing). Uncertain merges are skipped to preserve transparency.
///
/// Thread-safe: stateless and pure — safe to call from any thread.
/// </summary>
public static class RecommendationMergeEngine
{
    /// <summary>
    /// Evaluates a list of clusters and returns a new list with related
    /// clusters merged where appropriate.
    ///
    /// Unmerged clusters pass through unchanged. The list is re-sorted
    /// by MaxSeverity descending after merging.
    /// </summary>
    public static IReadOnlyList<RecommendationCluster> MergeRelated(
        IReadOnlyList<RecommendationCluster> clusters)
    {
        if (clusters.Count <= 1) return clusters;

        var result  = new List<RecommendationCluster>(clusters);
        var merged  = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // domains merged away

        TryMergePerformanceThermal(result, merged);
        TryMergeStorageRepair(result, merged);
        TryMergeStartupPerformance(result, merged);

        // Remove clusters that were absorbed into a merged cluster
        var final = result
            .Where(c => !merged.Contains(c.Domain))
            .OrderByDescending(c => (int)c.MaxSeverity)
            .ToList();

        return final.AsReadOnly();
    }

    // ── Merge strategies ──────────────────────────────────────────────────────

    private static void TryMergePerformanceThermal(
        List<RecommendationCluster> clusters,
        HashSet<string>             merged)
    {
        var performance = clusters.FirstOrDefault(c =>
            string.Equals(c.Domain, "Performance", StringComparison.OrdinalIgnoreCase));
        var thermal = clusters.FirstOrDefault(c =>
            string.Equals(c.Domain, "Thermal", StringComparison.OrdinalIgnoreCase));

        if (performance is null || thermal is null) return;

        // Only merge when both are present simultaneously AND have overlapping inference references
        var sharedInferences = performance.RelatedInferenceIds
            .Intersect(thermal.RelatedInferenceIds, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sharedInferences.Count == 0) return;

        // Build merged cluster with performance as the lead (typically higher severity)
        var allActions = performance.AllActions
            .Concat(thermal.AllActions)
            .OrderByDescending(a => a.PriorityScore)
            .ToList()
            .AsReadOnly();

        var allInferences = performance.RelatedInferenceIds
            .Union(thermal.RelatedInferenceIds, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();

        var maxSeverity = (RecommendationSeverity)Math.Max(
            (int)performance.MaxSeverity, (int)thermal.MaxSeverity);

        var idx = clusters.IndexOf(performance);
        clusters[idx] = performance with
        {
            Headline            = $"Performance & thermal — {allActions.Count} actions available",
            AllActions          = allActions,
            LeadAction          = allActions[0],
            MaxSeverity         = maxSeverity,
            RelatedInferenceIds = allInferences,
        };

        merged.Add("Thermal");
    }

    private static void TryMergeStorageRepair(
        List<RecommendationCluster> clusters,
        HashSet<string>             merged)
    {
        var storage = clusters.FirstOrDefault(c =>
            string.Equals(c.Domain, "Storage", StringComparison.OrdinalIgnoreCase));
        var repairs = clusters.FirstOrDefault(c =>
            string.Equals(c.Domain, "Repairs", StringComparison.OrdinalIgnoreCase));

        if (storage is null || repairs is null) return;

        // Only merge repair into storage when repair actions target storage-related issues
        bool repairsAreStorageRelated = repairs.AllActions.Any(a =>
            a.Action.Id.Contains("disk",    StringComparison.OrdinalIgnoreCase) ||
            a.Action.Id.Contains("storage", StringComparison.OrdinalIgnoreCase) ||
            a.Action.Id.Contains("repair",  StringComparison.OrdinalIgnoreCase));

        if (!repairsAreStorageRelated) return;

        var allActions = storage.AllActions
            .Concat(repairs.AllActions)
            .OrderByDescending(a => a.PriorityScore)
            .ToList()
            .AsReadOnly();

        var maxSeverity = (RecommendationSeverity)Math.Max(
            (int)storage.MaxSeverity, (int)repairs.MaxSeverity);

        var idx = clusters.IndexOf(storage);
        clusters[idx] = storage with
        {
            Headline    = $"Storage health — {allActions.Count} actions available",
            AllActions  = allActions,
            LeadAction  = allActions[0],
            MaxSeverity = maxSeverity,
        };

        merged.Add("Repairs");
    }

    private static void TryMergeStartupPerformance(
        List<RecommendationCluster> clusters,
        HashSet<string>             merged)
    {
        var startup = clusters.FirstOrDefault(c =>
            string.Equals(c.Domain, "Startup", StringComparison.OrdinalIgnoreCase));
        var performance = clusters.FirstOrDefault(c =>
            string.Equals(c.Domain, "Performance", StringComparison.OrdinalIgnoreCase));

        if (startup is null || performance is null) return;

        // Only merge when startup cluster has higher severity (driving the performance issue)
        if (startup.MaxSeverity < performance.MaxSeverity) return;

        var allActions = startup.AllActions
            .Concat(performance.AllActions)
            .OrderByDescending(a => a.PriorityScore)
            .ToList()
            .AsReadOnly();

        var idx = clusters.IndexOf(startup);
        clusters[idx] = startup with
        {
            Headline   = $"Startup optimization — {allActions.Count} actions available",
            AllActions = allActions,
            LeadAction = allActions[0],
        };

        merged.Add("Performance");
    }
}
