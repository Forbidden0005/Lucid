using Lucid.Services.Intelligence.Arbitration;

namespace Lucid.Services.Interaction.Language;

/// <summary>
/// Maps operational severity levels to calm, appropriately-scaled language.
///
/// Severity narration must never exaggerate urgency or use fear-based framing.
/// "Needs attention" — not "CRITICAL EMERGENCY". "Worth reviewing" — not "IN DANGER".
///
/// This engine is the single source of truth for severity-to-language mapping
/// in the Interaction layer. All presenters should call through here rather
/// than building their own severity strings.
///
/// Thread-safe: stateless and pure.
/// </summary>
public static class SeverityNarrativeEngine
{
    /// <summary>Returns a concise severity label for UI display (e.g., badge, chip).</summary>
    public static string GetLabel(RecommendationSeverity severity) => severity switch
    {
        RecommendationSeverity.Critical    => "Needs attention",
        RecommendationSeverity.Stability   => "Worth addressing",
        RecommendationSeverity.Performance => "Performance opportunity",
        RecommendationSeverity.Maintenance => "Maintenance item",
        RecommendationSeverity.Cosmetic    => "Minor improvement",
        _                                  => "Informational",
    };

    /// <summary>
    /// Returns a sentence-level urgency framing for recommendation cards.
    /// Calibrated to be calm and proportionate — never alarming.
    /// </summary>
    public static string GetUrgencyPhrase(RecommendationSeverity severity) => severity switch
    {
        RecommendationSeverity.Critical    =>
            "Acting on this may prevent further performance degradation.",
        RecommendationSeverity.Stability   =>
            "Addressing this would improve system stability.",
        RecommendationSeverity.Performance =>
            "This action may noticeably improve system performance.",
        RecommendationSeverity.Maintenance =>
            "This is a routine maintenance task.",
        RecommendationSeverity.Cosmetic    =>
            "This is a minor optional improvement.",
        _                                  => string.Empty,
    };

    /// <summary>Returns the relative priority word for a severity level.</summary>
    public static string GetPriorityWord(RecommendationSeverity severity) => severity switch
    {
        RecommendationSeverity.Critical    => "priority",
        RecommendationSeverity.Stability   => "recommended",
        RecommendationSeverity.Performance => "suggested",
        RecommendationSeverity.Maintenance => "optional",
        RecommendationSeverity.Cosmetic    => "cosmetic",
        _                                  => "informational",
    };

    /// <summary>
    /// Returns the color token (for design system mapping) for a severity level.
    /// Maps to the Lucid design system severity palette.
    /// </summary>
    public static string GetColorToken(RecommendationSeverity severity) => severity switch
    {
        RecommendationSeverity.Critical    => "Severity.Critical",
        RecommendationSeverity.Stability   => "Severity.Warning",
        RecommendationSeverity.Performance => "Severity.Advisory",
        RecommendationSeverity.Maintenance => "Severity.Maintenance",
        RecommendationSeverity.Cosmetic    => "Severity.Cosmetic",
        _                                  => "Severity.Info",
    };

    /// <summary>
    /// Returns a short context phrase used when explaining suppression.
    /// Example: "this maintenance recommendation was deferred during high system load."
    /// </summary>
    public static string GetSuppressionContext(RecommendationSeverity severity) => severity switch
    {
        RecommendationSeverity.Maintenance =>
            "this maintenance recommendation was deferred because higher-priority items are active",
        RecommendationSeverity.Cosmetic    =>
            "this cosmetic improvement was deferred to reduce information overload",
        RecommendationSeverity.Performance =>
            "this performance suggestion was held back while the system is under load",
        _                                  =>
            "this recommendation was deferred based on current system context",
    };
}
