using ExplainMyPC.Services.Intelligence;

namespace ExplainMyPC.ViewModels;

/// <summary>
/// Immutable presentation wrapper for a ranked system action produced by
/// <see cref="IGlobalRecommendationPrioritizer"/>.
///
/// Used on:
///   • Dashboard — Smart Actions card (top 3 ranked)
///   • Intelligence Workspace — Actions tab (full ranked list)
///
/// All color properties are hex strings that x:Bind maps directly to
/// Foreground/Background properties (consistent with SimulationSnapshotViewModel).
/// No converters required.
/// </summary>
public sealed class PrioritizedActionViewModel
{
    // ── Display properties ─────────────────────────────────────────────────────

    /// <summary>Short action title, e.g. "Clear temp files".</summary>
    public string Title { get; init; } = "";

    /// <summary>One-line plain-English description of what the action does.</summary>
    public string Description { get; init; } = "";

    /// <summary>Human-readable priority tier: "High priority" / "Medium priority" / "Lower priority".</summary>
    public string PriorityLabel { get; init; } = "";

    /// <summary>Hex color for PriorityLabel — green / amber / gray mapped from priority score tier.</summary>
    public string PriorityColor { get; init; } = "#6B7280";

    /// <summary>
    /// Effectiveness label from the learning profile, e.g.
    /// "Historically effective on this machine" or "No outcome data yet" when cold.
    /// </summary>
    public string EffectivenessLabel { get; init; } = "";

    /// <summary>Hex color for EffectivenessLabel — green when warm data exists, gray when cold.</summary>
    public string EffectivenessColor { get; init; } = "#6B7280";

    /// <summary>Title of the source insight that recommended this action (attribution).</summary>
    public string SourceInsightTitle { get; init; } = "";

    /// <summary>Segoe MDL2 Assets glyph for the action's impact tier.</summary>
    public string ImpactGlyph { get; init; } = "";

    /// <summary>Hex color for ImpactGlyph — blue for High/Significant, amber for Moderate, gray for Low.</summary>
    public string ImpactColor { get; init; } = "#6B7280";

    /// <summary>
    /// Human-readable explanation of why this action was ranked at its current level.
    /// Empty string when no personalization profile exists (cold start).
    /// </summary>
    public string WhyRecommended { get; init; } = "";

    /// <summary>True when WhyRecommended is non-empty.</summary>
    public bool HasWhyRecommended => WhyRecommended.Length > 0;

    /// <summary>
    /// Personalization multiplier from the score breakdown (formatted for display).
    /// e.g. "1.3×" when this category is boosted, "0.7×" when suppressed, "" when neutral.
    /// </summary>
    public string PersonalizationBadge { get; init; } = "";

    /// <summary>True when the PersonalizationBadge is non-empty (i.e., personalization is active).</summary>
    public bool HasPersonalizationBadge => PersonalizationBadge.Length > 0;

    /// <summary>Hex color for PersonalizationBadge (green = boosted, amber = suppressed).</summary>
    public string PersonalizationBadgeColor { get; init; } = "#6B7280";

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a <see cref="PrioritizedAction"/> record from the prioritizer
    /// to a display-ready ViewModel.
    /// </summary>
    public static PrioritizedActionViewModel From(PrioritizedAction ranked)
    {
        double pm = ranked.ScoreBreakdown.PersonalizationMultiplier;
        string pBadge = pm switch
        {
            > 1.15 => $"{pm:F1}× boosted",
            < 0.85 => $"{pm:F1}× suppressed",
            _      => "",
        };
        string pColor = pm > 1.15 ? "#34D399" : "#F59E0B";

        return new()
        {
            Title                    = ranked.Action.Title,
            Description              = ranked.Action.Description,
            PriorityLabel            = ranked.PriorityLabel,
            PriorityColor            = ToScoreColor(ranked.PriorityScore),
            EffectivenessLabel       = ranked.EffectivenessLabel,
            EffectivenessColor       = ToEffColor(ranked.EffectivenessLabel),
            SourceInsightTitle       = ranked.SourceInsight.Title,
            ImpactGlyph              = ToImpactGlyph(ranked.Action.Impact),
            ImpactColor              = ToImpactColor(ranked.Action.Impact),
            WhyRecommended           = ranked.WhyRecommended,
            PersonalizationBadge     = pBadge,
            PersonalizationBadgeColor = pColor,
        };
    }

    // ── Color / glyph helpers ──────────────────────────────────────────────────

    private static string ToScoreColor(double score) => score switch
    {
        >= 0.75 => "#34D399",   // green  — high priority
        >= 0.45 => "#F59E0B",   // amber  — medium priority
        _       => "#6B7280",   // gray   — lower priority
    };

    private static string ToEffColor(string label) =>
        label == "No outcome data yet" ? "#6B7280" : "#34D399";

    private static string ToImpactGlyph(ActionImpact impact) => impact switch
    {
        ActionImpact.Significant => "",  // lightning bolt
        ActionImpact.High        => "",  // gear / settings action
        ActionImpact.Moderate    => "",  // wrench
        _                        => "",  // info / diagnostic
    };

    private static string ToImpactColor(ActionImpact impact) => impact switch
    {
        ActionImpact.Significant => "#34D399",  // green
        ActionImpact.High        => "#4EA1FF",  // blue
        ActionImpact.Moderate    => "#F59E0B",  // amber
        _                        => "#6B7280",  // gray
    };
}
