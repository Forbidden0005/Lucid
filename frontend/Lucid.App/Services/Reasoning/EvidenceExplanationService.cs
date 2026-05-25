namespace Lucid.Services.Reasoning;

// ── Interface ─────────────────────────────────────────────────────────────────

/// <summary>
/// Produces concise, human-readable explanations for intelligence findings and
/// evidence chains.
///
/// Every explanation must be:
///   • Grounded — references only concrete observed evidence.
///   • Probabilistic — uses "likely", "appears", "observed" — not "is" or "definitely".
///   • Actionable — always includes a suggested next step.
///   • Honest about uncertainty — always explains what is not yet confirmed.
///
/// This service is stateless and pure — no background work, no side effects.
/// </summary>
public interface IEvidenceExplanationService
{
    /// <summary>
    /// Produces a structured explanation for a root cause candidate,
    /// including headline, evidence items, confidence reasoning, and next step.
    /// </summary>
    EvidenceExplanation ExplainCandidate(
        RootCauseCandidate candidate,
        EvidenceChain      supportingGraph);

    /// <summary>
    /// Produces a brief (2–3 sentence) narrative description of an evidence chain,
    /// suitable for display in a summary card.
    /// </summary>
    string SummarizeChain(EvidenceChain chain);

    /// <summary>
    /// Produces a one-sentence headline for a single evidence node.
    /// </summary>
    string HeadlineForNode(EvidenceNode node);
}

// ── Implementation ────────────────────────────────────────────────────────────

/// <summary>
/// Default implementation of <see cref="IEvidenceExplanationService"/>.
///
/// All text is deterministically generated from evidence properties.
/// No templates use placeholder markers (e.g. {metric}) — all values are
/// computed inline so every string is fully formed before being returned.
/// </summary>
public sealed class EvidenceExplanationService : IEvidenceExplanationService
{
    public EvidenceExplanation ExplainCandidate(
        RootCauseCandidate candidate,
        EvidenceChain      supportingGraph)
    {
        var evidenceItems = BuildEvidenceItems(candidate, supportingGraph);
        var headline      = BuildHeadline(candidate);
        var whyItMatters  = BuildWhyItMatters(candidate);
        var likelyOutcome = BuildLikelyOutcome(candidate, supportingGraph);
        var nextStep      = BuildNextStep(candidate);
        var confSummary   = BuildConfidenceSummary(candidate, supportingGraph);

        return new EvidenceExplanation
        {
            Headline          = headline,
            EvidenceItems     = evidenceItems,
            ConfidenceSummary = confSummary,
            WhyItMatters      = whyItMatters,
            LikelyOutcome     = likelyOutcome,
            SuggestedNextStep = nextStep,
        };
    }

    public string SummarizeChain(EvidenceChain chain)
    {
        if (chain.Nodes.Count == 0)
            return "No evidence observations are currently available for this chain.";

        var root = chain.RootNode;
        if (root is null)
            return $"The evidence graph contains {chain.Nodes.Count} observations without a clear root.";

        var causeCount = chain.Edges
            .Count(e => e.RelationshipType is EvidenceRelationshipType.Caused
                                           or EvidenceRelationshipType.ContributedTo);
        var actionCount = chain.Nodes.Count(n => n.NodeType == EvidenceNodeType.Action);

        var sb = new System.Text.StringBuilder();
        sb.Append($"The primary observation is: {root.Title}. ");

        if (causeCount > 0)
            sb.Append($"This is connected to {chain.Nodes.Count - 1} related evidence item{(chain.Nodes.Count > 2 ? "s" : "")} via {causeCount} causal link{(causeCount > 1 ? "s" : "")}. ");

        if (actionCount > 0)
            sb.Append($"{actionCount} remediation action{(actionCount > 1 ? "s were" : " was")} executed in this context.");

        return sb.ToString().TrimEnd();
    }

    public string HeadlineForNode(EvidenceNode node)
    {
        return node.NodeType switch
        {
            EvidenceNodeType.Warning       => $"Warning: {node.Title}",
            EvidenceNodeType.Anomaly       => $"Anomaly observed: {node.Title}",
            EvidenceNodeType.Drift         => $"Long-term drift: {node.Title}",
            EvidenceNodeType.Forecast      => $"Forecast: {node.Title}",
            EvidenceNodeType.Action        => $"Action taken: {node.Title}",
            EvidenceNodeType.Stabilization => $"Stabilized: {node.Title}",
            EvidenceNodeType.Insight       => $"Finding: {node.Title}",
            EvidenceNodeType.Telemetry     => $"Telemetry: {node.Title}",
            EvidenceNodeType.Process       => $"Process: {node.Title}",
            _                              => node.Title,
        };
    }

    // ── Private builders ──────────────────────────────────────────────────────

    private static string BuildHeadline(RootCauseCandidate candidate)
    {
        var conf = candidate.ConfidenceLabel;
        return $"{conf}: {candidate.CauseTitle}";
    }

    private static IReadOnlyList<string> BuildEvidenceItems(
        RootCauseCandidate candidate,
        EvidenceChain      graph)
    {
        var items = new List<string>();

        // From the candidate's own supporting evidence
        foreach (var evItem in candidate.SupportingEvidence.Take(3))
            items.Add(evItem);

        // Supplement with edge-relationship descriptions from the graph
        var edges = graph.Edges
            .Where(e => e.Confidence >= 0.55)
            .OrderByDescending(e => e.Confidence)
            .Take(3);

        foreach (var edge in edges)
        {
            var fromNode = graph.Nodes.FirstOrDefault(n => n.Id == edge.FromNodeId);
            var toNode   = graph.Nodes.FirstOrDefault(n => n.Id == edge.ToNodeId);
            if (fromNode is null || toNode is null) continue;

            var description =
                $"'{fromNode.Title}' {edge.RelationshipLabel} '{toNode.Title}' " +
                $"(confidence: {edge.Confidence:0%}).";
            if (!items.Contains(description))
                items.Add(description);
        }

        return items.Take(6).ToList();
    }

    private static string BuildWhyItMatters(RootCauseCandidate candidate)
    {
        var areas = candidate.AffectedSystems.Count > 0
            ? string.Join(", ", candidate.AffectedSystems)
            : "system performance";

        return $"This condition is affecting {areas}. " +
               $"If left unaddressed, performance or stability may continue to degrade. " +
               $"Addressing the root cause is more effective than treating individual symptoms.";
    }

    private static string BuildLikelyOutcome(
        RootCauseCandidate candidate,
        EvidenceChain      graph)
    {
        var hasForecasts = graph.Nodes.Any(n => n.NodeType == EvidenceNodeType.Forecast);
        var hasActions   = graph.Nodes.Any(n => n.NodeType == EvidenceNodeType.Action);

        if (hasForecasts && !hasActions)
            return "Forecast data suggests this condition is likely to escalate if no action is taken. " +
                   "Early intervention is recommended.";

        if (hasActions)
            return "A remediation action has already been taken in this context. " +
                   "Monitor system metrics over the next few minutes to verify whether conditions improve.";

        return "Without intervention, this condition may persist or worsen over time. " +
               "The recommended actions below are most likely to resolve the underlying cause.";
    }

    private static string BuildNextStep(RootCauseCandidate candidate)
    {
        if (candidate.RecommendedActionIds.Count > 0)
        {
            var actionLabel = HumanizeActionId(candidate.RecommendedActionIds[0]);
            return $"Recommended next step: {actionLabel}. " +
                   $"Navigate to the Repairs page to execute this action safely with full rollback support.";
        }

        if (candidate.AffectedSystems.Count > 0)
            return $"Review the {candidate.AffectedSystems[0]} section on the Dashboard for real-time " +
                   $"metrics and available remediation options.";

        return "Review the Intelligence findings on the Insights page to identify the most appropriate " +
               "remediation action for your current situation.";
    }

    private static string BuildConfidenceSummary(
        RootCauseCandidate candidate,
        EvidenceChain      graph)
    {
        var nodeCount  = graph.Nodes.Count;
        var edgeCount  = graph.Edges.Count;
        var confLabel  = candidate.ConfidenceLabel;

        return $"{confLabel} ({candidate.Confidence:0%}) — based on {nodeCount} evidence item{(nodeCount == 1 ? "" : "s")} " +
               $"and {edgeCount} causal relationship{(edgeCount == 1 ? "" : "s")} in the evidence graph. " +
               $"Confidence reflects data availability, not subjective assessment.";
    }

    private static string HumanizeActionId(string actionId)
    {
        // "action.disk.clean-temp-files" → "Clean temporary files"
        var parts = actionId.Split('.');
        if (parts.Length < 2) return actionId;
        var last = parts[^1]
            .Replace('-', ' ')
            .Replace('_', ' ');
        return char.ToUpperInvariant(last[0]) + last[1..];
    }
}
