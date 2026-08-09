using Lucid.Services.Intelligence.Arbitration;
using Lucid.Services.Interaction.Language;

namespace Lucid.Services.Interaction.Recommendations;

/// <summary>
/// A fully-rendered, lifecycle-tracked recommendation ready for UI presentation.
///
/// <see cref="UnifiedRecommendation"/> wraps a <see cref="RecommendationCluster"/>
/// with everything the ViewModel needs:
///   - Formatted headline and detail text (language-policy-compliant)
///   - Lifecycle state (active / suppressed / resolved / deferred / expired)
///   - Confidence and severity labels
///   - Explanation text for "Why is this being recommended?"
///   - Fatigue transparency ("Shown 4× without action")
///
/// Produced by <see cref="UnifiedRecommendationService"/> after each arbitration cycle.
/// Immutable — a new instance is created each cycle.
/// </summary>
public sealed record UnifiedRecommendation
{
    /// <summary>Stable cluster ID for deduplication and state tracking.</summary>
    public Guid ClusterId { get; init; }

    /// <summary>UTC time this recommendation was assembled.</summary>
    public DateTime AssembledAt { get; init; } = DateTime.UtcNow;

    // ── Display ───────────────────────────────────────────────────────────────

    /// <summary>Policy-compliant compact headline (≤ 120 chars).</summary>
    public string Headline { get; init; } = string.Empty;

    /// <summary>Policy-compliant standard summary (≤ 300 chars).</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Urgency phrase appropriate to the severity level.</summary>
    public string UrgencyPhrase { get; init; } = string.Empty;

    /// <summary>Human-readable severity label.</summary>
    public string SeverityLabel { get; init; } = string.Empty;

    /// <summary>Design-system color token for severity.</summary>
    public string SeverityColorToken { get; init; } = "Severity.Info";

    /// <summary>Operational domain of this cluster (e.g. "Performance", "Storage").</summary>
    public string Domain { get; init; } = string.Empty;

    // ── Classification ────────────────────────────────────────────────────────

    /// <summary>The severity of the lead action.</summary>
    public RecommendationSeverity Severity { get; init; }

    /// <summary>Priority score of the lead action.</summary>
    public double PriorityScore { get; init; }

    /// <summary>Number of actions in this cluster.</summary>
    public int ActionCount { get; init; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>Current lifecycle state of this recommendation.</summary>
    public RecommendationLifecycleState LifecycleState { get; init; } =
        RecommendationLifecycleState.Active;

    /// <summary>
    /// Fatigue transparency note.
    /// Non-empty when the fatigue engine has deprioritized this recommendation.
    /// Example: "Shown 4× in 7 days without action."
    /// </summary>
    public string FatigueNote { get; init; } = string.Empty;

    /// <summary>Explanation of why this was suppressed (when LifecycleState = Suppressed).</summary>
    public string? SuppressionExplanation { get; init; }

    // ── Explainability ────────────────────────────────────────────────────────

    /// <summary>
    /// "Why is this recommended?" — a brief explanation of the reasoning behind it.
    /// </summary>
    public string ReasoningExplanation { get; init; } = string.Empty;

    /// <summary>IDs of cognitive inferences that contributed to this recommendation.</summary>
    public IReadOnlyList<string> RelatedInferenceIds { get; init; } = [];

    // ── Source ────────────────────────────────────────────────────────────────

    /// <summary>The underlying arbitration cluster this record was built from.</summary>
    public RecommendationCluster SourceCluster { get; init; } = null!;

    // ── Computed ──────────────────────────────────────────────────────────────

    /// <summary>True when this recommendation is in the Active state.</summary>
    public bool IsActive => LifecycleState == RecommendationLifecycleState.Active;

    /// <summary>True when this recommendation has a fatigue note.</summary>
    public bool IsFatigued => !string.IsNullOrEmpty(FatigueNote);

    /// <summary>Display label for the action count (e.g. "1 action" / "3 actions").</summary>
    public string ActionCountLabel =>
        ActionCount == 1 ? "1 action" : $"{ActionCount} actions";
}
