namespace Lucid.Services.Intelligence.Patterns;

/// <summary>
/// Computes similarity scores between operational patterns.
///
/// Similarity is used to:
///   - Merge near-duplicate patterns (same type, overlapping domains)
///   - Group related patterns for the diagnostics view
///   - Prevent double-counting of correlated patterns
///
/// Similarity is scored in [0, 1]:
///   1.0 = identical patterns
///   0.0 = completely unrelated patterns
///
/// Similarity factors:
///   1. Pattern type match (dominant weight: 0.50)
///   2. Domain overlap (Jaccard coefficient: 0.30)
///   3. Workload context match (0.20)
///
/// Thread-safe: stateless and pure.
/// </summary>
public static class PatternSimilarityAnalyzer
{
    private const double TypeWeight     = 0.50;
    private const double DomainWeight   = 0.30;
    private const double WorkloadWeight = 0.20;

    /// <summary>
    /// Computes a similarity score [0, 1] between two patterns.
    /// Returns 1.0 for the same pattern key.
    /// </summary>
    public static double ComputeSimilarity(OperationalPattern a, OperationalPattern b)
    {
        if (a.PatternKey == b.PatternKey) return 1.0;

        var typeScore     = TypeSimilarity(a.PatternType, b.PatternType);
        var domainScore   = DomainSimilarity(a.Domains, b.Domains);
        var workloadScore = WorkloadSimilarity(a.DominantWorkloadContext, b.DominantWorkloadContext);

        return (typeScore * TypeWeight) + (domainScore * DomainWeight) + (workloadScore * WorkloadWeight);
    }

    /// <summary>
    /// Returns true when two patterns are similar enough to be considered related
    /// (similarity score ≥ <paramref name="threshold"/>).
    /// </summary>
    public static bool AreRelated(
        OperationalPattern a,
        OperationalPattern b,
        double             threshold = 0.65) =>
        ComputeSimilarity(a, b) >= threshold;

    /// <summary>
    /// Groups a list of patterns into clusters of related patterns.
    /// Patterns within a cluster all have pairwise similarity ≥ threshold.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<OperationalPattern>> Cluster(
        IReadOnlyList<OperationalPattern> patterns,
        double                            threshold = 0.65)
    {
        if (patterns.Count == 0) return [];

        var clusters  = new List<List<OperationalPattern>>();
        var assigned  = new HashSet<string>();

        foreach (var pattern in patterns)
        {
            if (assigned.Contains(pattern.PatternKey)) continue;

            var cluster = new List<OperationalPattern> { pattern };
            assigned.Add(pattern.PatternKey);

            foreach (var other in patterns)
            {
                if (assigned.Contains(other.PatternKey)) continue;
                if (AreRelated(pattern, other, threshold))
                {
                    cluster.Add(other);
                    assigned.Add(other.PatternKey);
                }
            }

            clusters.Add(cluster);
        }

        return clusters.Select(c => (IReadOnlyList<OperationalPattern>)c.AsReadOnly())
                       .ToList()
                       .AsReadOnly();
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static double TypeSimilarity(string typeA, string typeB)
    {
        if (string.Equals(typeA, typeB, StringComparison.OrdinalIgnoreCase)) return 1.0;

        // Partial type similarity: same prefix = related types.
        var prefixA = typeA.Length > 4 ? typeA[..4] : typeA;
        var prefixB = typeB.Length > 4 ? typeB[..4] : typeB;
        return string.Equals(prefixA, prefixB, StringComparison.OrdinalIgnoreCase) ? 0.5 : 0.0;
    }

    private static double DomainSimilarity(
        IReadOnlyList<string> domainsA,
        IReadOnlyList<string> domainsB)
    {
        if (domainsA.Count == 0 && domainsB.Count == 0) return 1.0;
        if (domainsA.Count == 0 || domainsB.Count == 0) return 0.0;

        // Jaccard coefficient: |intersection| / |union|
        var setA        = new HashSet<string>(domainsA, StringComparer.OrdinalIgnoreCase);
        var setB        = new HashSet<string>(domainsB, StringComparer.OrdinalIgnoreCase);
        var intersection = setA.Intersect(setB).Count();
        var union        = setA.Union(setB).Count();

        return union == 0 ? 0 : (double)intersection / union;
    }

    private static double WorkloadSimilarity(string? contextA, string? contextB)
    {
        if (contextA is null && contextB is null) return 0.5; // both unknown — neutral
        if (contextA is null || contextB is null) return 0.2; // one known, one unknown
        return string.Equals(contextA, contextB, StringComparison.OrdinalIgnoreCase)
            ? 1.0 : 0.0;
    }
}
