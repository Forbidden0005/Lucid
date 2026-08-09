using Lucid.Services.Intelligence;

namespace Lucid.Services.Intelligence.Arbitration;

/// <summary>
/// A group of related <see cref="SystemAction"/> recommendations that share
/// a common root cause or operational domain.
///
/// Clustering reduces UI noise by presenting grouped, related recommendations
/// as a single coherent card rather than N separate items.
///
/// Examples:
///   - "3 startup management actions" → one cluster
///   - "Thermal and CPU pressure actions" → one cluster
///
/// Immutable — built once per arbitration cycle, never mutated.
/// </summary>
public sealed record RecommendationCluster
{
    /// <summary>Unique identifier for this cluster instance.</summary>
    public Guid ClusterId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Short headline summarizing what this cluster recommends.
    /// Example: "Startup congestion — 3 actions available"
    /// </summary>
    public string Headline { get; init; } = string.Empty;

    /// <summary>
    /// Domain category all cluster members share.
    /// Example: "Startup", "Thermal", "Storage", "Performance".
    /// </summary>
    public string Domain { get; init; } = string.Empty;

    /// <summary>Highest severity among all members of this cluster.</summary>
    public RecommendationSeverity MaxSeverity { get; init; }

    /// <summary>
    /// The prioritized action that best represents this cluster.
    /// Shown as the primary action when the cluster is collapsed.
    /// </summary>
    public PrioritizedAction LeadAction { get; init; } = null!;

    /// <summary>
    /// All actions in this cluster, including the lead action, ordered by priority.
    /// </summary>
    public IReadOnlyList<PrioritizedAction> AllActions { get; init; } = [];

    /// <summary>
    /// Evidence inference IDs that this cluster addresses.
    /// Used for drill-down navigation to the reasoning page.
    /// </summary>
    public IReadOnlyList<string> RelatedInferenceIds { get; init; } = [];

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>True if this cluster has more than one action.</summary>
    public bool IsGrouped => AllActions.Count > 1;

    /// <summary>Display label for action count, e.g. "3 actions".</summary>
    public string ActionCountLabel =>
        AllActions.Count == 1 ? "1 action" : $"{AllActions.Count} actions";
}
