using Lucid.Services.Reasoning.Cognitive;

namespace Lucid.Services.Interaction.Language;

/// <summary>
/// Adjusts communication tone and hedging language based on confidence level.
///
/// Lucid must never claim certainty it doesn't have. This class maps each
/// confidence tier to appropriate epistemic hedges — from "Lucid has detected"
/// (Certain) down to "Lucid observed a possible signal" (Speculative).
///
/// Usage pattern:
///   var lead  = ConfidenceToneAdjuster.GetLeadingPhrase(confidence);
///   var hedge = ConfidenceToneAdjuster.GetHedgeClause(confidence);
///   var text  = $"{lead} {coreMessage}. {hedge}".Trim();
///
/// Thread-safe: stateless and pure.
/// </summary>
public static class ConfidenceToneAdjuster
{
    /// <summary>
    /// Returns a leading phrase appropriate for the confidence level.
    /// Used to open explanation sentences.
    /// </summary>
    public static string GetLeadingPhrase(ConfidenceLevel confidence) => confidence switch
    {
        ConfidenceLevel.Certain     => "Lucid has detected",
        ConfidenceLevel.High        => "Evidence strongly suggests",
        ConfidenceLevel.Medium      => "Data indicates",
        ConfidenceLevel.Low         => "Lucid noticed a possible pattern —",
        ConfidenceLevel.Speculative => "Lucid observed a weak signal —",
        _                           => "Analysis indicates",
    };

    /// <summary>
    /// Returns a hedge clause to append when confidence is below High.
    /// Null for Certain and High — no hedge is needed.
    /// </summary>
    public static string? GetHedgeClause(ConfidenceLevel confidence) => confidence switch
    {
        ConfidenceLevel.Certain     => null,
        ConfidenceLevel.High        => null,
        ConfidenceLevel.Medium      =>
            "Confidence is moderate — more data may refine this assessment.",
        ConfidenceLevel.Low         =>
            "Limited evidence means this observation may be transient.",
        ConfidenceLevel.Speculative =>
            "Insufficient data to draw reliable conclusions — flagged for transparency only.",
        _                           => null,
    };

    /// <summary>
    /// Returns a human-readable description of what the confidence level means.
    /// Used in explainability UI panels.
    /// </summary>
    public static string ExplainConfidenceLevel(ConfidenceLevel confidence) => confidence switch
    {
        ConfidenceLevel.Certain     => "Multiple independent signals agree on this finding.",
        ConfidenceLevel.High        => "Strong evidence with minor ambiguity.",
        ConfidenceLevel.Medium      => "Reasonable evidence, but notable uncertainty remains.",
        ConfidenceLevel.Low         => "Weak or limited evidence — pattern is possible but unclear.",
        ConfidenceLevel.Speculative => "Speculative — flagged only for transparency. Not a recommendation.",
        _                           => "Confidence level unavailable.",
    };

    /// <summary>
    /// Returns the appropriate verb form for a confidence level.
    /// Used for sentence construction: "Lucid [verb] that…"
    /// </summary>
    public static string GetVerbForm(ConfidenceLevel confidence) => confidence switch
    {
        ConfidenceLevel.Certain     => "has determined",
        ConfidenceLevel.High        => "is confident",
        ConfidenceLevel.Medium      => "believes",
        ConfidenceLevel.Low         => "suspects",
        ConfidenceLevel.Speculative => "has noticed",
        _                           => "indicates",
    };

    /// <summary>
    /// Adjusts a pre-written sentence to match the given confidence level
    /// by prepending the appropriate epistemic lead phrase.
    /// </summary>
    public static string ApplyTone(string sentence, ConfidenceLevel confidence)
    {
        if (string.IsNullOrWhiteSpace(sentence)) return sentence;
        var lead  = GetLeadingPhrase(confidence);
        var hedge = GetHedgeClause(confidence);

        var body = sentence.TrimEnd('.');
        return hedge is not null
            ? $"{lead} {body}. {hedge}"
            : $"{lead} {body}.";
    }
}
