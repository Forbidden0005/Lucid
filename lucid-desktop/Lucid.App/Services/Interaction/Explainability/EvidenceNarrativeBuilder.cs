using Lucid.Services.Interaction.Language;
using Lucid.Services.Reasoning;
using Lucid.Services.Reasoning.Cognitive;

namespace Lucid.Services.Interaction.Explainability;

/// <summary>
/// Builds human-readable prose summaries from <see cref="ReasoningEvidence"/> chains.
///
/// Evidence narration answers the user question:
///   "What did Lucid actually look at to reach this conclusion?"
///
/// Rules:
///   - Always enumerate evidence sources clearly
///   - Distinguish between direct telemetry readings and derived insight signals
///   - Qualify weak evidence with appropriate hedging language
///   - Never claim more certainty than the evidence supports
///
/// Thread-safe: stateless and pure.
/// </summary>
public static class EvidenceNarrativeBuilder
{
    /// <summary>
    /// Builds a compact, single-sentence evidence summary.
    /// Suitable for card subtitles and tooltips.
    /// </summary>
    public static string BuildCompact(IReadOnlyList<ReasoningEvidence> evidence)
    {
        if (evidence.Count == 0)
            return "No specific evidence recorded for this inference.";

        var sources = evidence.Take(2).Select(DescribeSource).ToList();
        var summary = sources.Count == 1
            ? $"Based on {sources[0]}"
            : $"Based on {sources[0]} and {sources[1]}";

        if (evidence.Count > 2)
            summary += $" (+{evidence.Count - 2} more)";

        return CalmCommunicationFormatter.FormatCompact(summary);
    }

    /// <summary>
    /// Builds a full, multi-sentence evidence narrative.
    /// Used in expanded explainability panels.
    /// </summary>
    public static string BuildFull(IReadOnlyList<ReasoningEvidence> evidence)
    {
        if (evidence.Count == 0)
            return "No specific evidence was recorded for this inference.";

        var parts = new List<string>(evidence.Count + 1);
        parts.Add($"This inference was supported by {evidence.Count} evidence " +
                  $"source{(evidence.Count == 1 ? "" : "s")}:");

        foreach (var e in evidence)
        {
            var strength = DescribeStrength(e.Strength);
            parts.Add($"• {DescribeSource(e)} ({strength})");
        }

        return CalmCommunicationFormatter.FormatDetail(string.Join("\n", parts));
    }

    /// <summary>
    /// Builds a structured list of evidence items for rich UI presentation.
    /// Returns a list of (source description, strength label) tuples.
    /// </summary>
    public static IReadOnlyList<(string Source, string Strength)> BuildStructured(
        IReadOnlyList<ReasoningEvidence> evidence) =>
        evidence
            .Select(e => (DescribeSource(e), DescribeStrength(e.Strength)))
            .ToList()
            .AsReadOnly();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string DescribeSource(ReasoningEvidence e) => e.NodeType switch
    {
        EvidenceNodeType.Telemetry    => $"live telemetry reading — {e.Description}",
        EvidenceNodeType.Insight      => $"intelligence finding — {e.Description}",
        EvidenceNodeType.Anomaly      => $"behavioral anomaly — {e.Description}",
        EvidenceNodeType.Forecast     => $"trajectory forecast — {e.Description}",
        EvidenceNodeType.Process      => $"process attribution — {e.Description}",
        EvidenceNodeType.Drift        => $"behavioral drift — {e.Description}",
        EvidenceNodeType.Warning      => $"early warning — {e.Description}",
        _                             => e.Description,
    };

    private static string DescribeStrength(EvidenceStrength strength) => strength switch
    {
        EvidenceStrength.Strong        => "strong signal",
        EvidenceStrength.Moderate      => "moderate signal",
        EvidenceStrength.Weak          => "weak signal",
        EvidenceStrength.Circumstantial => "circumstantial",
        _                              => "supporting signal",
    };
}
