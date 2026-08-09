using Lucid.Services.Interaction.Language;
using Lucid.Services.Reasoning.Cognitive;

namespace Lucid.Services.Interaction.Explainability;

/// <summary>
/// Builds human-readable prose that explains a <see cref="ConfidenceScore"/> to the user.
///
/// Confidence narration answers:
///   "How certain is Lucid about this finding, and why?"
///
/// The goal is radical transparency — the user should never have to wonder why
/// a finding is marked "moderate confidence" or "low confidence". This builder
/// turns the math of ConfidenceScore.Rationale into plain English.
///
/// Thread-safe: stateless and pure.
/// </summary>
public static class ConfidenceNarrativeBuilder
{
    /// <summary>
    /// Builds a compact (≤ 120 char) confidence sentence for tooltips and labels.
    /// </summary>
    public static string BuildCompact(ConfidenceScore score)
    {
        var levelText = ConfidenceToneAdjuster.ExplainConfidenceLevel(score.Level);
        return CalmCommunicationFormatter.FormatCompact(
            $"{score.Percentage} confidence — {levelText}");
    }

    /// <summary>
    /// Builds a standard-length (≤ 300 char) confidence summary for card bodies.
    /// </summary>
    public static string BuildStandard(ConfidenceScore score)
    {
        var parts = new List<string>(3);

        // Lead with the level meaning.
        parts.Add(ConfidenceToneAdjuster.ExplainConfidenceLevel(score.Level));

        // Explain any corroboration boost.
        if (score.CorroborationBoost > 0.01)
            parts.Add($"Multiple signals agree ({score.CorroborationBoost:+#.0%;-#.0%;0%} boost).");

        // Summarize uncertainty if present.
        if (score.UncertaintyFactors.Count > 0)
        {
            var count = score.UncertaintyFactors.Count;
            parts.Add($"{count} uncertainty factor{(count == 1 ? "" : "s")} reduced the score.");
        }

        return CalmCommunicationFormatter.FormatStandard(string.Join(" ", parts));
    }

    /// <summary>
    /// Builds a full diagnostic confidence explanation including the computation rationale.
    /// Used in the Explainability panel.
    /// </summary>
    public static string BuildFull(ConfidenceScore score)
    {
        var parts = new List<string>(5);

        parts.Add(ConfidenceToneAdjuster.ExplainConfidenceLevel(score.Level));

        if (score.CorroborationBoost > 0.01)
            parts.Add(
                $"Confidence boosted by {score.CorroborationBoost:P0} from corroborating signals.");

        if (score.UncertaintyFactors.Count > 0)
        {
            foreach (var f in score.UncertaintyFactors)
                parts.Add($"Reduced by {f.Penalty:P0} due to: {f.Label}.");
        }

        // Append the full computation rationale chain.
        if (!string.IsNullOrWhiteSpace(score.Rationale))
            parts.Add($"Computation: {score.Rationale}");

        // Append the hedge clause if applicable.
        var hedge = ConfidenceToneAdjuster.GetHedgeClause(score.Level);
        if (hedge is not null)
            parts.Add(hedge);

        return CalmCommunicationFormatter.FormatDetail(string.Join(" ", parts));
    }
}
