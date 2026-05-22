using ExplainMyPC.Services.History;
using ExplainMyPC.Services.Intelligence;
using ExplainMyPC.Services.Session;

namespace ExplainMyPC.Services.Reasoning;

// ── Interface ─────────────────────────────────────────────────────────────────

/// <summary>
/// Builds and queries a causal evidence graph connecting telemetry observations,
/// intelligence findings, anomaly detections, forecasts, executed actions, and
/// stabilization events into traversable causal chains.
///
/// All reasoning is deterministic, local-only, and traceable:
///   • Edges are probabilistic (confidence 0–1), never asserted as certainty.
///   • Causality is always stated as "likely caused" or "contributed to".
///   • No opaque scoring — every relationship has an explicit typed reason.
///
/// Threading: all public methods are safe to call from any thread.
///            BuildGraphAsync() is CPU-bound (synchronous internally) but
///            wrapped in Task.Run to keep the UI thread unblocked.
/// </summary>
public interface IOperationalEvidenceGraph
{
    /// <summary>
    /// Builds (or rebuilds) the current evidence graph from all available
    /// intelligence sources: active insights, anomalies, drifts, early warnings,
    /// forecasts, session events, and recent operation history.
    ///
    /// Replaces the in-memory graph atomically on completion.
    /// Safe to call multiple times — each call produces a fresh immutable snapshot.
    /// </summary>
    Task<EvidenceChain> BuildGraphAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns all evidence nodes connected (directly or transitively) to the
    /// given root node, along with the edges that link them.
    ///
    /// Returns an empty chain when the node ID is not in the current graph.
    /// </summary>
    EvidenceChain GetConnectedEvidence(string rootNodeId);

    /// <summary>
    /// Returns all nodes whose <see cref="EvidenceNode.NodeType"/> is
    /// <see cref="EvidenceNodeType.Warning"/> or
    /// <see cref="EvidenceNodeType.Anomaly"/> — the highest-priority roots
    /// for root-cause analysis.
    /// </summary>
    IReadOnlyList<EvidenceNode> GetHighPriorityRoots();

    /// <summary>The most recently built complete graph. Null before the first build.</summary>
    EvidenceChain? CurrentGraph { get; }
}

// ── Implementation ────────────────────────────────────────────────────────────

/// <summary>
/// Default implementation of <see cref="IOperationalEvidenceGraph"/>.
///
/// Graph construction algorithm:
///   1. Materialise nodes from all intelligence sources (telemetry, insights,
///      anomalies, drifts, warnings, actions, forecasts, stabilisation events,
///      session events).
///   2. Link nodes with directed typed edges based on co-occurrence timing,
///      shared insight IDs, and source-to-outcome relationships.
///   3. Prune stale nodes older than the retention window (2 hours by default).
///   4. Return the graph as an immutable EvidenceChain.
///
/// Memory bound: ≤ 300 nodes and ≤ 600 edges per graph.
/// </summary>
public sealed class OperationalEvidenceGraph : IOperationalEvidenceGraph
{
    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly ISystemInsightEngine       _insights;
    private readonly IOperationHistoryService   _history;
    private readonly ISessionContextService     _session;
    private readonly IAnomalyDetectionService   _anomalyDetection;
    private readonly IDriftDetectionService     _driftDetection;
    private readonly IEarlyWarningService       _earlyWarning;

    // ── Config ────────────────────────────────────────────────────────────────

    private const int    MaxNodes              = 300;
    private const int    MaxEdges              = 600;
    private static readonly TimeSpan NodeRetention = TimeSpan.FromHours(2);

    // ── State ─────────────────────────────────────────────────────────────────

    private volatile EvidenceChain? _currentGraph;

    public EvidenceChain? CurrentGraph => _currentGraph;

    // ── Constructor ───────────────────────────────────────────────────────────

    public OperationalEvidenceGraph(
        ISystemInsightEngine     insights,
        IOperationHistoryService history,
        ISessionContextService   session,
        IAnomalyDetectionService anomalyDetection,
        IDriftDetectionService   driftDetection,
        IEarlyWarningService     earlyWarning)
    {
        _insights         = insights;
        _history          = history;
        _session          = session;
        _anomalyDetection = anomalyDetection;
        _driftDetection   = driftDetection;
        _earlyWarning     = earlyWarning;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<EvidenceChain> BuildGraphAsync(CancellationToken ct = default)
    {
        var chain = await Task.Run(() => BuildInternal(ct), ct).ConfigureAwait(false);
        _currentGraph = chain;
        return chain;
    }

    public EvidenceChain GetConnectedEvidence(string rootNodeId)
    {
        var graph = _currentGraph;
        if (graph is null || !graph.Nodes.Any(n => n.Id == rootNodeId))
            return new EvidenceChain { ComputedAt = DateTimeOffset.Now, RootNodeId = rootNodeId };

        // BFS traversal — follow edges in both directions
        var visited  = new HashSet<string>();
        var queue    = new Queue<string>();
        var nodes    = new List<EvidenceNode>();
        var edges    = new List<EvidenceEdge>();

        queue.Enqueue(rootNodeId);
        visited.Add(rootNodeId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var node    = graph.Nodes.FirstOrDefault(n => n.Id == current);
            if (node is not null) nodes.Add(node);

            foreach (var edge in graph.Edges.Where(e => e.FromNodeId == current || e.ToNodeId == current))
            {
                edges.Add(edge);
                var other = edge.FromNodeId == current ? edge.ToNodeId : edge.FromNodeId;
                if (visited.Add(other)) queue.Enqueue(other);
            }
        }

        return new EvidenceChain
        {
            Nodes      = nodes,
            Edges      = edges,
            ComputedAt = graph.ComputedAt,
            RootNodeId = rootNodeId,
        };
    }

    public IReadOnlyList<EvidenceNode> GetHighPriorityRoots()
    {
        var graph = _currentGraph;
        if (graph is null) return [];

        return graph.Nodes
            .Where(n => n.NodeType is EvidenceNodeType.Warning or EvidenceNodeType.Anomaly)
            .OrderByDescending(n => n.Confidence)
            .ToList();
    }

    // ── Graph construction ────────────────────────────────────────────────────

    private EvidenceChain BuildInternal(CancellationToken ct)
    {
        var now      = DateTimeOffset.Now;
        var cutoff   = now - NodeRetention;
        var nodes    = new List<EvidenceNode>(64);
        var edges    = new List<EvidenceEdge>(128);
        var nodeById = new Dictionary<string, EvidenceNode>(64);

        ct.ThrowIfCancellationRequested();

        // ── Step 1: Materialise nodes ──────────────────────────────────────────

        // Active intelligence insights → Insight nodes
        var activeInsights = _insights.CurrentInsights;
        foreach (var insight in activeInsights)
        {
            var nodeId = "insight:" + insight.Id;
            var node   = new EvidenceNode
            {
                Id         = nodeId,
                NodeType   = ClassifyInsightNodeType(insight),
                Title      = insight.Title,
                Summary    = TruncateSummary(insight.Detail),
                Confidence = insight.ConfidencePercent / 100.0,
                CreatedAt  = insight.DetectedAt,
                SourceId   = insight.Id,
            };
            AddNode(nodes, nodeById, node);
        }

        ct.ThrowIfCancellationRequested();

        // Active anomaly detections → Anomaly nodes
        // DetectedAnomaly: Metric, Description, DetectedAt, DivergenceScore, Severity
        var anomalies = _anomalyDetection.CurrentAnomalies;
        foreach (var anomaly in anomalies)
        {
            var nodeId = "anomaly:" + Stable32(anomaly.Metric + anomaly.DetectedAt.Ticks);
            // Map DivergenceScore to confidence: 2σ → ~0.70, 3σ → ~0.90
            var confidence = Math.Min(0.50 + (Math.Abs(anomaly.DivergenceScore) / 10.0), 0.92);
            var node   = new EvidenceNode
            {
                Id         = nodeId,
                NodeType   = EvidenceNodeType.Anomaly,
                Title      = $"{anomaly.Metric} anomaly detected",
                Summary    = anomaly.Description,
                Confidence = confidence,
                CreatedAt  = new DateTimeOffset(anomaly.DetectedAt, TimeSpan.Zero),
                SourceId   = anomaly.Metric,
            };
            AddNode(nodes, nodeById, node);
        }

        // Drift detections → Drift nodes
        // DetectedDrift: MetricName, Summary, Severity, ChangePercent (no DetectedAt — always "now")
        var drifts = _driftDetection.CurrentDrifts;
        foreach (var drift in drifts)
        {
            var nodeId = "drift:" + Stable32(drift.MetricName);
            // Confidence from severity
            var confidence = drift.Severity switch
            {
                AnomalyIntelligenceSeverity.High     => 0.85,
                AnomalyIntelligenceSeverity.Elevated => 0.72,
                AnomalyIntelligenceSeverity.Moderate => 0.60,
                _                                    => 0.45,
            };
            var node   = new EvidenceNode
            {
                Id         = nodeId,
                NodeType   = EvidenceNodeType.Drift,
                Title      = $"Long-term {drift.MetricName} drift",
                Summary    = drift.Summary,
                Confidence = confidence,
                CreatedAt  = DateTimeOffset.Now,
                SourceId   = drift.MetricName,
            };
            AddNode(nodes, nodeById, node);
        }

        ct.ThrowIfCancellationRequested();

        // Early warnings → Warning nodes
        // EarlyWarning: WarningId, Title, Explanation, Severity, ConfidencePercent, DetectedAt
        var warnings = _earlyWarning.CurrentWarnings;
        foreach (var warning in warnings)
        {
            var detectedAt = new DateTimeOffset(warning.DetectedAt, TimeSpan.Zero);
            if (detectedAt < cutoff) continue;
            var nodeId = "warning:" + Stable32(warning.WarningId);
            var node   = new EvidenceNode
            {
                Id         = nodeId,
                NodeType   = EvidenceNodeType.Warning,
                Title      = warning.Title,
                Summary    = warning.Explanation,
                Confidence = warning.ConfidencePercent / 100.0,
                CreatedAt  = detectedAt,
                SourceId   = warning.AffectedMetric,
            };
            AddNode(nodes, nodeById, node);
        }

        // Session events → Telemetry nodes (boot, idle, wake)
        var sessionEvents = _session.Current.RecentEvents;
        foreach (var ev in sessionEvents)
        {
            if (ev.OccurredAt < cutoff) continue;
            var nodeId = "session:" + Stable32(ev.Kind.ToString() + ev.OccurredAt.Ticks);
            var node   = new EvidenceNode
            {
                Id         = nodeId,
                NodeType   = EvidenceNodeType.Telemetry,
                Title      = SessionEventTitle(ev.Kind),
                Summary    = SessionEventSummary(ev, now),
                Confidence = 1.0,
                CreatedAt  = ev.OccurredAt,
                SourceId   = ev.AssociatedId,
            };
            AddNode(nodes, nodeById, node);
        }

        ct.ThrowIfCancellationRequested();

        // Recent operations → Action and Stabilization nodes
        var recentOps = _history.GetRecentAsync(20).GetAwaiter().GetResult();
        foreach (var op in recentOps)
        {
            if (op.ExecutedAt < cutoff) continue;
            var nodeId = "action:" + op.Id;
            var type   = op.IsRollback
                ? EvidenceNodeType.Stabilization
                : EvidenceNodeType.Action;
            var node   = new EvidenceNode
            {
                Id         = nodeId,
                NodeType   = type,
                Title      = op.IsRollback ? $"Rolled back: {op.ActionTitle}" : op.ActionTitle,
                Summary    = $"{(op.IsSuccess ? "Succeeded" : "Failed")} in {op.DurationMs} ms. {op.Message}",
                Confidence = op.IsSuccess ? 0.95 : 0.5,
                CreatedAt  = op.ExecutedAt,
                SourceId   = op.Id,
            };
            AddNode(nodes, nodeById, node);
        }

        ct.ThrowIfCancellationRequested();

        // Forecast insights → Forecast nodes (by insight ID prefix)
        foreach (var insight in activeInsights.Where(i => i.Id.Contains("forecast")))
        {
            var fNodeId = "forecast:" + insight.Id;
            if (nodeById.ContainsKey(fNodeId)) continue;
            var fNode   = new EvidenceNode
            {
                Id         = fNodeId,
                NodeType   = EvidenceNodeType.Forecast,
                Title      = insight.Title,
                Summary    = TruncateSummary(insight.Detail),
                Confidence = insight.ConfidencePercent / 100.0,
                CreatedAt  = insight.DetectedAt,
                SourceId   = insight.Id,
            };
            AddNode(nodes, nodeById, fNode);
        }

        ct.ThrowIfCancellationRequested();

        // ── Step 2: Build edges ────────────────────────────────────────────────

        BuildEdges(nodes, edges, nodeById, now);

        ct.ThrowIfCancellationRequested();

        // ── Step 3: Enforce bounds ─────────────────────────────────────────────

        if (nodes.Count > MaxNodes)
        {
            var trimmedIds = nodes
                .OrderByDescending(n => n.Confidence)
                .ThenByDescending(n => n.CreatedAt)
                .Take(MaxNodes)
                .Select(n => n.Id)
                .ToHashSet();
            nodes.RemoveAll(n => !trimmedIds.Contains(n.Id));
        }

        if (edges.Count > MaxEdges)
            edges.RemoveRange(MaxEdges, edges.Count - MaxEdges);

        // ── Step 4: Select root ────────────────────────────────────────────────

        var rootNode = nodes
            .Where(n => n.NodeType is EvidenceNodeType.Warning or EvidenceNodeType.Anomaly)
            .OrderByDescending(n => n.Confidence)
            .FirstOrDefault()
            ?? nodes.OrderByDescending(n => n.Confidence).FirstOrDefault();

        return new EvidenceChain
        {
            Nodes      = nodes,
            Edges      = edges,
            ComputedAt = now,
            RootNodeId = rootNode?.Id ?? string.Empty,
        };
    }

    private void BuildEdges(
        List<EvidenceNode>              nodes,
        List<EvidenceEdge>              edges,
        Dictionary<string, EvidenceNode> nodeById,
        DateTimeOffset                  now)
    {
        // Rule 1: Warning nodes → correlated Anomaly nodes (temporal co-occurrence, ≤ 10 min)
        var warnNodes  = nodes.Where(n => n.NodeType == EvidenceNodeType.Warning).ToList();
        var anomalyNds = nodes.Where(n => n.NodeType == EvidenceNodeType.Anomaly).ToList();

        foreach (var warn in warnNodes)
        foreach (var anom in anomalyNds)
        {
            if (Math.Abs((warn.CreatedAt - anom.CreatedAt).TotalMinutes) <= 10)
                AddEdge(edges, warn.Id, anom.Id, EvidenceRelationshipType.Caused, 0.65);
        }

        // Rule 2: Anomaly/Warning nodes → Forecast nodes (anomalies can forecast)
        var forecastNodes = nodes.Where(n => n.NodeType == EvidenceNodeType.Forecast).ToList();
        foreach (var src in nodes.Where(n => n.NodeType is EvidenceNodeType.Anomaly or EvidenceNodeType.Warning))
        foreach (var fcast in forecastNodes)
        {
            // Only link if they share a metric keyword
            if (SharesKeyword(src.Title, fcast.Title))
                AddEdge(edges, src.Id, fcast.Id, EvidenceRelationshipType.Forecasted, 0.60);
        }

        // Rule 3: Insight nodes → their forecast counterparts
        var insightNodes = nodes.Where(n => n.NodeType == EvidenceNodeType.Insight).ToList();
        foreach (var insNode in insightNodes)
        {
            var insightId = insNode.SourceId;
            if (insightId is null) continue;
            foreach (var fcast in forecastNodes.Where(f => f.SourceId == insightId))
                AddEdge(edges, insNode.Id, fcast.Id, EvidenceRelationshipType.Forecasted, 0.75);
        }

        // Rule 4: Action nodes temporally follow a Warning/Anomaly
        var actionNodes = nodes.Where(n => n.NodeType == EvidenceNodeType.Action).ToList();
        var causeNodes  = nodes.Where(n =>
            n.NodeType is EvidenceNodeType.Warning
                       or EvidenceNodeType.Anomaly
                       or EvidenceNodeType.Insight).ToList();

        foreach (var action in actionNodes)
        foreach (var cause in causeNodes.Where(c => c.CreatedAt <= action.CreatedAt))
        {
            // Only link if action followed cause within 60 minutes
            if ((action.CreatedAt - cause.CreatedAt).TotalMinutes <= 60)
                AddEdge(edges, cause.Id, action.Id, EvidenceRelationshipType.ContributedTo, 0.55);
        }

        // Rule 5: Successful Action nodes → Stabilization / correlated warnings resolved
        var stabNodes = nodes.Where(n => n.NodeType == EvidenceNodeType.Stabilization).ToList();
        foreach (var action in actionNodes)
        foreach (var stab in stabNodes.Where(s => s.CreatedAt >= action.CreatedAt))
        {
            if ((stab.CreatedAt - action.CreatedAt).TotalMinutes <= 30)
                AddEdge(edges, action.Id, stab.Id, EvidenceRelationshipType.ResolvedBy, 0.70);
        }

        // Rule 6: Drift nodes contribute to Warning nodes
        var driftNodes = nodes.Where(n => n.NodeType == EvidenceNodeType.Drift).ToList();
        foreach (var drift in driftNodes)
        foreach (var warn in warnNodes)
        {
            if (SharesKeyword(drift.SourceId ?? drift.Title, warn.Title))
                AddEdge(edges, drift.Id, warn.Id, EvidenceRelationshipType.ContributedTo, 0.60);
        }

        // Rule 7: Session events → Insight onset co-occurrence (within 5 min of session event)
        var sessionNodes = nodes.Where(n => n.NodeType == EvidenceNodeType.Telemetry).ToList();
        foreach (var sess in sessionNodes)
        foreach (var ins in insightNodes)
        {
            var gap = (ins.CreatedAt - sess.CreatedAt).TotalMinutes;
            if (gap >= 0 && gap <= 5)
                AddEdge(edges, sess.Id, ins.Id, EvidenceRelationshipType.CorrelatedWith, 0.50);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AddNode(
        List<EvidenceNode>              nodes,
        Dictionary<string, EvidenceNode> nodeById,
        EvidenceNode                    node)
    {
        if (!nodeById.ContainsKey(node.Id))
        {
            nodes.Add(node);
            nodeById[node.Id] = node;
        }
    }

    private static void AddEdge(
        List<EvidenceEdge>        edges,
        string                    from,
        string                    to,
        EvidenceRelationshipType  type,
        double                    confidence)
    {
        // Avoid duplicate edges for the same pair + relationship
        if (!edges.Any(e => e.FromNodeId == from && e.ToNodeId == to && e.RelationshipType == type))
        {
            edges.Add(new EvidenceEdge
            {
                FromNodeId       = from,
                ToNodeId         = to,
                RelationshipType = type,
                Confidence       = confidence,
            });
        }
    }

    private static EvidenceNodeType ClassifyInsightNodeType(SystemInsight insight)
    {
        if (insight.Id.Contains("forecast")) return EvidenceNodeType.Forecast;
        return insight.Severity switch
        {
            InsightSeverity.Warning        => EvidenceNodeType.Anomaly,
            InsightSeverity.Recommendation => EvidenceNodeType.Insight,
            _                              => EvidenceNodeType.Insight,
        };
    }

    private static string TruncateSummary(string text, int maxLen = 200)
        => text.Length <= maxLen ? text : text[..maxLen].TrimEnd() + "…";

    private static string SessionEventTitle(SessionEventKind kind) => kind switch
    {
        SessionEventKind.SystemBoot      => "System booted",
        SessionEventKind.AppSessionStart => "ExplainMyPC session started",
        SessionEventKind.WakeFromSleep   => "System woke from sleep",
        SessionEventKind.IdleBegin       => "System entered idle state",
        SessionEventKind.IdleEnd         => "System returned from idle",
        SessionEventKind.InsightOnset    => "Intelligence finding appeared",
        SessionEventKind.InsightCleared  => "Intelligence finding cleared",
        _                                => "Session event",
    };

    private static string SessionEventSummary(SessionEvent ev, DateTimeOffset now)
    {
        var ago = (now - ev.OccurredAt).TotalMinutes;
        var when = ago < 1 ? "just now"
                 : ago < 60 ? $"{(int)ago} min ago"
                 : $"{(int)(ago / 60)} hr ago";
        return $"{SessionEventTitle(ev.Kind)} — {when}.";
    }

    private static bool SharesKeyword(string a, string b)
    {
        static string[] Words(string s) =>
            s.ToLowerInvariant().Split(' ', '-', '_', '.', '/');

        var keywords = new HashSet<string> { "cpu", "ram", "memory", "disk", "thermal", "gpu",
            "temperature", "startup", "process", "network" };
        var wa = Words(a);
        var wb = Words(b);
        return wa.Intersect(wb).Any(w => keywords.Contains(w));
    }

    /// <summary>Produces a stable 32-char hex ID from an arbitrary string.</summary>
    private static string Stable32(string key)
    {
        var hash = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(key));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant(); // 32 hex chars
    }
}
