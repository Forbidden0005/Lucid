using ExplainMyPC.Services.Intelligence;

namespace ExplainMyPC.Services.Reasoning;

// ── Interface ─────────────────────────────────────────────────────────────────

/// <summary>
/// Analyzes the operational evidence graph to identify the most probable root
/// causes of current system degradation.
///
/// All analysis is:
///   • Deterministic — same inputs always produce the same outputs.
///   • Confidence-aware — all candidates carry explicit confidence scores.
///   • Causal, not correlational — edges represent plausible causal paths.
///   • Transparent — every candidate explains its supporting evidence.
///
/// Limitations the engine openly acknowledges:
///   • Confidence is bounded at 0.92 (never claims certainty).
///   • When evidence is thin, fewer candidates are emitted.
///   • All findings are probabilistic — not assertions of fact.
/// </summary>
public interface IRootCauseAnalysisEngine
{
    /// <summary>
    /// Analyzes the given <paramref name="graph"/> and returns an ordered list
    /// of root-cause candidates from most to least probable.
    ///
    /// Returns an empty list when the graph contains no evidence.
    /// Never returns more than <paramref name="maxCandidates"/> items.
    /// </summary>
    IReadOnlyList<RootCauseCandidate> AnalyzeRootCauses(
        EvidenceChain graph,
        int           maxCandidates = 5);

    /// <summary>
    /// Returns the single highest-confidence root cause from
    /// <see cref="AnalyzeRootCauses"/>, or null when no candidates exist.
    /// </summary>
    RootCauseCandidate? GetPrimaryRootCause(EvidenceChain graph);
}

// ── Implementation ────────────────────────────────────────────────────────────

/// <summary>
/// Deterministic root cause analysis via evidence graph traversal.
///
/// Algorithm:
///   1. Identify candidate root nodes — Warning, Anomaly, and Drift nodes with
///      no strong incoming causal edges (i.e. they are not effects of other nodes).
///   2. Score each candidate by aggregating the confidence of all downstream
///      evidence connected to it via causal or correlational edges.
///   3. Gather supporting evidence text from connected insight nodes.
///   4. Infer affected system areas from node titles and types.
///   5. Map to recommended action IDs from any Insight nodes in the chain.
///   6. Return candidates ordered by composite confidence score.
/// </summary>
public sealed class RootCauseAnalysisEngine : IRootCauseAnalysisEngine
{
    // Max confidence cap — the system never claims certainty about root causes
    private const double MaxConfidence = 0.92;

    public IReadOnlyList<RootCauseCandidate> AnalyzeRootCauses(
        EvidenceChain graph,
        int           maxCandidates = 5)
    {
        if (graph.Nodes.Count == 0) return [];

        var candidates = new List<RootCauseCandidate>();

        // Identify nodes that could be root causes: Warning, Anomaly, Drift
        var rootCandidateTypes = new HashSet<EvidenceNodeType>
        {
            EvidenceNodeType.Warning,
            EvidenceNodeType.Anomaly,
            EvidenceNodeType.Drift,
        };

        // Build an in-degree map (count of incoming causal edges per node)
        var inDegree = new Dictionary<string, int>(graph.Nodes.Count);
        foreach (var node in graph.Nodes) inDegree[node.Id] = 0;

        foreach (var edge in graph.Edges.Where(e =>
            e.RelationshipType is EvidenceRelationshipType.Caused
                               or EvidenceRelationshipType.ContributedTo))
        {
            if (inDegree.ContainsKey(edge.ToNodeId))
                inDegree[edge.ToNodeId]++;
        }

        // Candidate roots = Warning/Anomaly/Drift with low incoming degree
        // and high own confidence
        var rootNodes = graph.Nodes
            .Where(n => rootCandidateTypes.Contains(n.NodeType))
            .Where(n => inDegree.GetValueOrDefault(n.Id) <= 1)
            .OrderByDescending(n => n.Confidence)
            .ToList();

        // If no root candidates found, fall back to any Warning/Anomaly
        if (rootNodes.Count == 0)
        {
            rootNodes = graph.Nodes
                .Where(n => n.NodeType is EvidenceNodeType.Warning or EvidenceNodeType.Anomaly)
                .OrderByDescending(n => n.Confidence)
                .ToList();
        }

        // Analyze each candidate
        foreach (var rootNode in rootNodes.Take(maxCandidates * 2))
        {
            var candidate = BuildCandidate(rootNode, graph);
            if (candidate is not null) candidates.Add(candidate);
        }

        // Sort by confidence descending and cap
        return candidates
            .OrderByDescending(c => c.Confidence)
            .Take(maxCandidates)
            .ToList();
    }

    public RootCauseCandidate? GetPrimaryRootCause(EvidenceChain graph)
    {
        var candidates = AnalyzeRootCauses(graph, maxCandidates: 1);
        return candidates.Count > 0 ? candidates[0] : null;
    }

    // ── Candidate builder ─────────────────────────────────────────────────────

    private static RootCauseCandidate? BuildCandidate(
        EvidenceNode  rootNode,
        EvidenceChain graph)
    {
        // BFS from root through outgoing causal edges to gather downstream evidence
        var visited         = new HashSet<string> { rootNode.Id };
        var queue           = new Queue<string>();
        var downstreamNodes = new List<EvidenceNode>();
        var supportingEvid  = new List<string>();
        var affectedAreas   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var actionIds       = new List<string>();
        double confidenceAcc = rootNode.Confidence;
        int    evidenceCount = 1;

        queue.Enqueue(rootNode.Id);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();

            // Follow outgoing causal/correlational edges
            var outEdges = graph.Edges.Where(e =>
                e.FromNodeId == currentId &&
                e.RelationshipType is EvidenceRelationshipType.Caused
                                   or EvidenceRelationshipType.ContributedTo
                                   or EvidenceRelationshipType.CorrelatedWith
                                   or EvidenceRelationshipType.Forecasted);

            foreach (var edge in outEdges)
            {
                if (!visited.Add(edge.ToNodeId)) continue;

                var connNode = graph.Nodes.FirstOrDefault(n => n.Id == edge.ToNodeId);
                if (connNode is null) continue;

                downstreamNodes.Add(connNode);
                queue.Enqueue(edge.ToNodeId);

                // Accumulate confidence from connected evidence
                confidenceAcc += connNode.Confidence * edge.Confidence;
                evidenceCount++;

                // Gather supporting evidence text
                if (connNode.NodeType is EvidenceNodeType.Insight
                                      or EvidenceNodeType.Anomaly
                                      or EvidenceNodeType.Drift
                                      or EvidenceNodeType.Warning)
                {
                    supportingEvid.Add($"{connNode.Title}: {TruncateSummary(connNode.Summary)}");
                }
            }
        }

        // Infer affected system areas from all connected node titles
        foreach (var node in downstreamNodes.Prepend(rootNode))
            CollectSystemAreas(node.Title + " " + node.Summary, affectedAreas);

        // Map to action IDs from any Insight nodes in the chain
        foreach (var node in downstreamNodes.Where(n => n.NodeType == EvidenceNodeType.Insight))
        {
            // SourceId on Insight nodes = insight rule ID (e.g. "ram.pressure")
            // Derive a likely action ID from the insight ID
            if (node.SourceId is not null)
            {
                var derived = DeriveActionId(node.SourceId);
                if (derived is not null) actionIds.Add(derived);
            }
        }

        // Normalise confidence: average across accumulated evidence, capped
        double normalised = Math.Min(confidenceAcc / evidenceCount, MaxConfidence);

        // Require minimum confidence to avoid noise candidates
        if (normalised < 0.35) return null;

        return new RootCauseCandidate
        {
            CauseTitle            = BuildCauseTitle(rootNode),
            Explanation           = BuildExplanation(rootNode, downstreamNodes),
            SupportingEvidence    = supportingEvid.Take(5).ToList(),
            Confidence            = normalised,
            AffectedSystems       = affectedAreas.Take(4).ToList(),
            RecommendedActionIds  = actionIds.Distinct().Take(3).ToList(),
            HistoricalOccurrences = 0,   // updated by LearningService integration in future
        };
    }

    // ── Text helpers ──────────────────────────────────────────────────────────

    private static string BuildCauseTitle(EvidenceNode node) => node.NodeType switch
    {
        EvidenceNodeType.Warning => node.Title,
        EvidenceNodeType.Anomaly => $"Behavioral anomaly: {node.Title}",
        EvidenceNodeType.Drift   => $"Long-term drift: {node.Title}",
        _                        => node.Title,
    };

    private static string BuildExplanation(EvidenceNode root, List<EvidenceNode> downstream)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(root.Summary.Length > 0 ? root.Summary : root.Title);

        var insightCount = downstream.Count(n => n.NodeType is EvidenceNodeType.Insight
                                                              or EvidenceNodeType.Anomaly);
        if (insightCount > 0)
            sb.Append($" This finding is supported by {insightCount} related intelligence observation{(insightCount == 1 ? "" : "s")}.");

        var forecastCount = downstream.Count(n => n.NodeType == EvidenceNodeType.Forecast);
        if (forecastCount > 0)
            sb.Append(" Forecast data suggests this condition may escalate if not addressed.");

        return sb.ToString();
    }

    private static void CollectSystemAreas(string text, HashSet<string> areas)
    {
        var lower = text.ToLowerInvariant();
        if (lower.Contains("cpu") || lower.Contains("processor"))  areas.Add("CPU");
        if (lower.Contains("ram") || lower.Contains("memory"))     areas.Add("RAM");
        if (lower.Contains("disk") || lower.Contains("storage"))   areas.Add("Disk");
        if (lower.Contains("thermal") || lower.Contains("temp"))   areas.Add("Thermal");
        if (lower.Contains("gpu") || lower.Contains("graphics"))   areas.Add("GPU");
        if (lower.Contains("startup") || lower.Contains("boot"))   areas.Add("Startup");
        if (lower.Contains("network") || lower.Contains("dns"))    areas.Add("Network");
    }

    private static string? DeriveActionId(string insightId)
    {
        // Map insight IDs to plausible action IDs
        // e.g. "ram.pressure" → "action.ram.free-memory"
        //      "disk.low-space" → "action.disk.clean-temp-files"
        var id = insightId.ToLowerInvariant();
        if (id.Contains("ram") || id.Contains("memory"))  return "action.ram.free-memory";
        if (id.Contains("disk") && id.Contains("exhaust")) return "action.disk.clean-temp-files";
        if (id.Contains("disk") && id.Contains("low"))     return "action.disk.clean-temp-files";
        if (id.Contains("startup"))                         return "action.startup.review-startup-items";
        if (id.Contains("cpu") && id.Contains("high"))     return "action.cpu.open-task-manager";
        if (id.Contains("thermal") || id.Contains("temp")) return "action.thermal.open-task-manager";
        return null;
    }

    private static string TruncateSummary(string text, int maxLen = 120)
        => text.Length <= maxLen ? text : text[..maxLen].TrimEnd() + "…";
}
