using Lucid.Services.Interaction.Language;
using Lucid.Services.Reasoning.Cognitive;

namespace Lucid.Services.Interaction.Presentation;

/// <summary>
/// Formats <see cref="ConfidenceScore"/> objects into human-readable explanations
/// for use in the Explainability UX panels.
///
/// Produces two levels of explanation:
///   - Compact: a single sentence describing what confidence means (e.g. for tooltip)
///   - Full: a multi-sentence breakdown showing how the score was computed
///
/// Thread-safe: stateless and pure.
/// </summary>
public static class ConfidenceExplanationFormatter
{
    /// <summary>
    /// Returns a compact, single-sentence confidence description.
    /// Suitable for tooltips, chips, and inline labels.
    /// </summary>
    public static string FormatCompact(ConfidenceScore score)
    {
        var levelExplanation = ConfidenceToneAdjuster.ExplainConfidenceLevel(score.Level);
        return CalmCommunicationFormatter.FormatCompact(
            $"{score.Percentage} confidence — {levelExplanation}");
    }

    /// <summary>
    /// Returns a full multi-sentence explanation of how the confidence was computed.
    /// Used in the expanded explainability panel.
    /// </summary>
    public static string FormatFull(ConfidenceScore score)
    {
        var parts = new List<string>(4);

        // Lead with the level meaning.
        parts.Add(ConfidenceToneAdjuster.ExplainConfidenceLevel(score.Level));

        // Add breakdown if meaningful.
        if (score.CorroborationBoost > 0.01)
            parts.Add($"Confidence was boosted by {score.CorroborationBoost:P0} " +
                      "due to multiple corroborating signals.");

        if (score.UncertaintyFactors.Count > 0)
        {
            var factors = score.UncertaintyFactors.Select(f => f.Label).ToList();
            var joined  = factors.Count == 1
                ? factors[0]
                : string.Join(", ", factors[..^1]) + " and " + factors[^1];
            parts.Add($"Confidence was reduced by uncertainty factors: {joined}.");
        }

        // Append the full rationale chain from the ConfidenceScore itself.
        if (!string.IsNullOrWhiteSpace(score.Rationale))
            parts.Add($"Score computation: {score.Rationale}");

        return CalmCommunicationFormatter.FormatDetail(string.Join(" ", parts));
    }

    /// <summary>
    /// Returns a hedge clause based on the confidence level, or null for high/certain.
    /// Used to append uncertainty acknowledgements to recommendations.
    /// </summary>
    public static string? GetHedge(ConfidenceScore score) =>
        ConfidenceToneAdjuster.GetHedgeClause(score.Level);
}
