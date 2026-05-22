using System.Text;
using ExplainMyPC.Services.Intelligence;
using ExplainMyPC.Services.Narrative;
using ExplainMyPC.Services.Reasoning;
using ExplainMyPC.Services.Timeline;

namespace ExplainMyPC.Services.Companion;

/// <summary>
/// Deterministic intent-resolution engine for the operational companion.
///
/// Intent is resolved via keyword matching — not machine learning.
/// Responses are composed from real service data: insights, narrative,
/// timeline events, and the evidence graph.
///
/// Response philosophy:
///   - Always state uncertainty where it exists
///   - Ground every claim in a specific data source
///   - Suggest where to look for more detail (which page)
///   - Never claim more confidence than the underlying data supports
/// </summary>
public sealed class OperationalConversationEngine : IOperationalConversationEngine
{
    // ── Dependencies ───────────────────────────────────────────────────────────

    private readonly ISystemInsightEngine        _insights;
    private readonly IOperationalNarrativeEngine _narrative;
    private readonly ITimelineAggregationService _timeline;
    private readonly IOperationalEvidenceGraph   _evidenceGraph;
    private readonly IRootCauseAnalysisEngine    _rootCauseEngine;

    public OperationalConversationEngine(
        ISystemInsightEngine        insights,
        IOperationalNarrativeEngine narrative,
        ITimelineAggregationService timeline,
        IOperationalEvidenceGraph   evidenceGraph,
        IRootCauseAnalysisEngine    rootCauseEngine)
    {
        _insights        = insights;
        _narrative       = narrative;
        _timeline        = timeline;
        _evidenceGraph   = evidenceGraph;
        _rootCauseEngine = rootCauseEngine;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public async Task<CompanionMessage> ProcessQueryAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var intent   = ResolveIntent(query);
            var category = ClassifyCategory(intent);

            var text = intent switch
            {
                QueryIntent.Performance   => await Task.Run(() => ComposePerformanceResponse(),  ct),
                QueryIntent.Memory        => await Task.Run(() => ComposeMemoryResponse(),       ct),
                QueryIntent.Disk          => await Task.Run(() => ComposeDiskResponse(),         ct),
                QueryIntent.Cpu           => await Task.Run(() => ComposeCpuResponse(),          ct),
                QueryIntent.Startup       => await Task.Run(() => ComposeStartupResponse(),      ct),
                QueryIntent.Security      => await Task.Run(() => ComposeSecurityResponse(),     ct),
                QueryIntent.Timeline      => await Task.Run(() => ComposeTimelineResponse(),     ct),
                QueryIntent.Investigation => await ComposeInvestigationResponseAsync(ct),
                QueryIntent.Help          => BuildHelpText(),
                _                         => await Task.Run(() => ComposeStatusResponse(),       ct),
            };

            return BuildMessage(text, category);
        }
        catch (OperationCanceledException)
        {
            return BuildErrorMessage("Analysis was cancelled.");
        }
        catch (Exception ex)
        {
            return BuildErrorMessage($"Could not complete analysis: {ex.Message}");
        }
    }

    // ── Intent resolution ──────────────────────────────────────────────────────

    private static QueryIntent ResolveIntent(string query)
    {
        var q = query.ToLowerInvariant();

        if (HasAny(q, "help", "what can you", "can you", "what do you do", "what are you"))
            return QueryIntent.Help;
        if (HasAny(q, "investigate", "root cause", "root-cause", "primary cause", "what is causing"))
            return QueryIntent.Investigation;
        if (HasAny(q, "slow", "lag", "sluggish", "freeze", "performance", "unresponsive", "hanging"))
            return QueryIntent.Performance;
        if (HasAny(q, "ram", "memory", "heap", "out of memory"))
            return QueryIntent.Memory;
        if (HasAny(q, "disk", "storage", "drive", "space", "ssd", "hdd", "nvme"))
            return QueryIntent.Disk;
        if (HasAny(q, "cpu", "processor", "core", "compute"))
            return QueryIntent.Cpu;
        if (HasAny(q, "startup", "boot", "start up", "launch time", "login", "logon"))
            return QueryIntent.Startup;
        if (HasAny(q, "security", "risk", "suspicious", "threat", "malware", "unusual", "flagged"))
            return QueryIntent.Security;
        if (HasAny(q, "changed", "recent", "today", "history", "what happened", "timeline", "event"))
            return QueryIntent.Timeline;

        return QueryIntent.Status;
    }

    private static bool HasAny(string text, params string[] terms)
        => terms.Any(t => text.Contains(t, StringComparison.Ordinal));

    private static CompanionMessageCategory ClassifyCategory(QueryIntent intent) => intent switch
    {
        QueryIntent.Investigation => CompanionMessageCategory.Insight,
        QueryIntent.Performance   => CompanionMessageCategory.Warning,
        QueryIntent.Memory        => CompanionMessageCategory.Warning,
        QueryIntent.Disk          => CompanionMessageCategory.Warning,
        QueryIntent.Cpu           => CompanionMessageCategory.Warning,
        QueryIntent.Security      => CompanionMessageCategory.Warning,
        QueryIntent.Startup       => CompanionMessageCategory.Insight,
        QueryIntent.Timeline      => CompanionMessageCategory.Insight,
        _                         => CompanionMessageCategory.Answer,
    };

    // ── Response composers ─────────────────────────────────────────────────────

    private string ComposeStatusResponse()
    {
        var narrative = _narrative.CurrentNarrative;
        if (narrative is null)
            return "Telemetry is still initializing — check back in a few seconds once " +
                   "the monitoring engine has collected its first readings.";

        var sb = new StringBuilder();
        sb.AppendLine(narrative.Headline);
        sb.AppendLine();
        sb.Append(narrative.StatusParagraph);

        var warnings = _insights.CurrentInsights
            .Where(i => i.Severity == InsightSeverity.Warning)
            .Take(3)
            .ToList();

        if (warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append($"{warnings.Count} active warning{(warnings.Count > 1 ? "s" : "")}: ");
            sb.Append(string.Join("; ", warnings.Select(w => w.Title.ToLowerInvariant())));
            sb.Append('.');
        }

        return sb.ToString().TrimEnd();
    }

    private string ComposePerformanceResponse()
    {
        var insights = _insights.CurrentInsights
            .Where(i => i.Severity >= InsightSeverity.Recommendation)
            .OrderByDescending(i => i.Severity)
            .Take(5)
            .ToList();

        if (insights.Count == 0)
        {
            var n = _narrative.CurrentNarrative;
            return n is not null
                ? $"Performance looks stable. {n.Headline}\n\n{n.StatusParagraph}"
                : "No active performance findings. Your system appears to be operating within " +
                  "normal ranges based on its established baseline.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"{insights.Count} active finding{(insights.Count > 1 ? "s" : "")} " +
                      "that may explain the slowdown:");
        sb.AppendLine();

        foreach (var insight in insights)
        {
            sb.AppendLine($"• {insight.Title} ({insight.ConfidencePercent}% confidence)");
            sb.AppendLine($"  {insight.Detail}");
            if (insight.ActionHint is not null)
                sb.AppendLine($"  → {insight.ActionHint}");
            sb.AppendLine();
        }

        sb.Append("Open the Investigation page for a full causal analysis and guided remediation.");
        return sb.ToString().TrimEnd();
    }

    private string ComposeMemoryResponse()
    {
        var ramInsights = _insights.CurrentInsights
            .Where(i => i.Id.Contains("ram", StringComparison.OrdinalIgnoreCase)
                     || i.Id.Contains("memory", StringComparison.OrdinalIgnoreCase)
                     || i.Title.Contains("RAM", StringComparison.OrdinalIgnoreCase)
                     || i.Title.Contains("Memory", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Severity)
            .ToList();

        if (ramInsights.Count == 0)
            return "No active memory pressure findings. RAM usage is within expected ranges " +
                   "for this machine's baseline profile.";

        var sb = new StringBuilder();
        sb.AppendLine($"Memory analysis — {ramInsights.Count} " +
                      $"finding{(ramInsights.Count > 1 ? "s" : "")}:");
        sb.AppendLine();

        foreach (var insight in ramInsights)
        {
            sb.AppendLine($"• {insight.Title}");
            sb.AppendLine($"  {insight.Detail}");
            if (insight.ActionHint is not null)
                sb.AppendLine($"  → {insight.ActionHint}");
            sb.AppendLine();
        }

        sb.Append("Open the Processes page to identify which processes are consuming RAM.");
        return sb.ToString().TrimEnd();
    }

    private string ComposeDiskResponse()
    {
        var diskInsights = _insights.CurrentInsights
            .Where(i => i.Id.Contains("disk", StringComparison.OrdinalIgnoreCase)
                     || i.Id.Contains("storage", StringComparison.OrdinalIgnoreCase)
                     || i.Title.Contains("Disk", StringComparison.OrdinalIgnoreCase)
                     || i.Title.Contains("Storage", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Severity)
            .ToList();

        if (diskInsights.Count == 0)
            return "No active disk or storage findings. Disk space and I/O throughput are " +
                   "within expected ranges based on this machine's baseline.";

        var sb = new StringBuilder();
        sb.AppendLine($"Disk analysis — {diskInsights.Count} " +
                      $"finding{(diskInsights.Count > 1 ? "s" : "")}:");
        sb.AppendLine();

        foreach (var insight in diskInsights)
        {
            sb.AppendLine($"• {insight.Title}");
            sb.AppendLine($"  {insight.Detail}");
            if (insight.ActionHint is not null)
                sb.AppendLine($"  → {insight.ActionHint}");
            sb.AppendLine();
        }

        sb.Append("Open the Storage page to identify large files and duplicate groups.");
        return sb.ToString().TrimEnd();
    }

    private string ComposeCpuResponse()
    {
        var cpuInsights = _insights.CurrentInsights
            .Where(i => i.Id.Contains("cpu", StringComparison.OrdinalIgnoreCase)
                     || i.Id.Contains("thermal", StringComparison.OrdinalIgnoreCase)
                     || i.Title.Contains("CPU", StringComparison.OrdinalIgnoreCase)
                     || i.Title.Contains("Temperature", StringComparison.OrdinalIgnoreCase)
                     || i.Title.Contains("Thermal", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Severity)
            .ToList();

        if (cpuInsights.Count == 0)
            return "No active CPU or thermal findings. Processor usage and temperature are " +
                   "within expected ranges for this machine's baseline profile.";

        var sb = new StringBuilder();
        sb.AppendLine($"CPU analysis — {cpuInsights.Count} " +
                      $"finding{(cpuInsights.Count > 1 ? "s" : "")}:");
        sb.AppendLine();

        foreach (var insight in cpuInsights)
        {
            sb.AppendLine($"• {insight.Title}");
            sb.AppendLine($"  {insight.Detail}");
            if (insight.ActionHint is not null)
                sb.AppendLine($"  → {insight.ActionHint}");
            sb.AppendLine();
        }

        sb.Append("Open the Processes page to identify which processes are consuming CPU.");
        return sb.ToString().TrimEnd();
    }

    private string ComposeStartupResponse()
    {
        var startupInsights = _insights.CurrentInsights
            .Where(i => i.Id.Contains("startup", StringComparison.OrdinalIgnoreCase)
                     || i.Title.Contains("Startup", StringComparison.OrdinalIgnoreCase)
                     || i.Title.Contains("Boot", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Severity)
            .ToList();

        if (startupInsights.Count == 0)
            return "No active startup findings. Your startup configuration appears reasonable " +
                   "for this machine's profile.";

        var sb = new StringBuilder();
        sb.AppendLine($"Startup analysis — {startupInsights.Count} " +
                      $"finding{(startupInsights.Count > 1 ? "s" : "")}:");
        sb.AppendLine();

        foreach (var insight in startupInsights)
        {
            sb.AppendLine($"• {insight.Title}");
            sb.AppendLine($"  {insight.Detail}");
            if (insight.ActionHint is not null)
                sb.AppendLine($"  → {insight.ActionHint}");
            sb.AppendLine();
        }

        sb.Append("Use the Repairs page to manage startup items with full rollback support.");
        return sb.ToString().TrimEnd();
    }

    private string ComposeSecurityResponse()
    {
        var secInsights = _insights.CurrentInsights
            .Where(i => i.Id.Contains("security", StringComparison.OrdinalIgnoreCase)
                     || i.Title.Contains("Security", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Severity)
            .ToList();

        if (secInsights.Count == 0)
            return "No active security observations. The security intelligence engine has not " +
                   "flagged any unusual patterns in the current session.\n\n" +
                   "Use the Security page for a full persistence and signature scan.";

        var sb = new StringBuilder();
        sb.AppendLine($"Security observations — {secInsights.Count} " +
                      $"finding{(secInsights.Count > 1 ? "s" : "")}:");
        sb.AppendLine();

        foreach (var insight in secInsights)
        {
            sb.AppendLine($"• {insight.Title} (confidence: {insight.ConfidencePercent}%)");
            sb.AppendLine($"  {insight.Detail}");
            if (insight.ActionHint is not null)
                sb.AppendLine($"  → {insight.ActionHint}");
            sb.AppendLine();
        }

        sb.Append("These are observations, not verdicts. Review each finding in context before acting. " +
                  "Open the Security page for full details and signature verification.");
        return sb.ToString().TrimEnd();
    }

    private string ComposeTimelineResponse()
    {
        var cutoff = DateTimeOffset.Now.AddHours(-24);
        var recentEvents = _timeline.Events
            .Where(e => e.OccurredAt >= cutoff)
            .OrderByDescending(e => e.OccurredAt)
            .Take(8)
            .ToList();

        if (recentEvents.Count == 0)
            return "No timeline events recorded in the last 24 hours. " +
                   "The timeline builds as insights fire, actions execute, and session state changes.";

        var sb = new StringBuilder();
        sb.AppendLine($"Last 24 hours — {recentEvents.Count} timeline " +
                      $"event{(recentEvents.Count > 1 ? "s" : "")}:");
        sb.AppendLine();

        foreach (var ev in recentEvents)
        {
            var timeAgo = FormatTimeAgo(ev.OccurredAt);
            sb.AppendLine($"• [{timeAgo}] {ev.Title}");
            if (!string.IsNullOrEmpty(ev.Detail))
                sb.AppendLine($"  {TruncateAt(ev.Detail, 100)}");
        }

        sb.Append("Open the Timeline page to browse the full operational history.");
        return sb.ToString().TrimEnd();
    }

    private async Task<string> ComposeInvestigationResponseAsync(CancellationToken ct)
    {
        // Use cached graph if available, otherwise build one
        var graph = _evidenceGraph.CurrentGraph
            ?? await _evidenceGraph.BuildGraphAsync(ct).ConfigureAwait(false);

        var candidates = _rootCauseEngine.AnalyzeRootCauses(graph, maxCandidates: 3);

        if (candidates.Count == 0)
        {
            return "The evidence graph does not contain enough signal to isolate a primary " +
                   "root cause at this time.\n\n" +
                   "Ensure the monitoring engine has been running for at least a few minutes " +
                   "so insights, anomalies, and drifts can accumulate. " +
                   "Check the Investigation page for the full evidence chain.";
        }

        var primary = candidates[0];
        var sb      = new StringBuilder();

        sb.AppendLine($"Root cause investigation — {graph.Nodes.Count} evidence " +
                      $"node{(graph.Nodes.Count == 1 ? "" : "s")}, " +
                      $"{graph.Edges.Count} causal link{(graph.Edges.Count == 1 ? "" : "s")}:");
        sb.AppendLine();
        sb.AppendLine($"Primary candidate ({primary.ConfidenceLabel}):");
        sb.AppendLine($"• {primary.CauseTitle}");
        sb.AppendLine($"  {primary.Explanation}");
        sb.AppendLine();

        if (primary.SupportingEvidence.Count > 0)
        {
            sb.AppendLine("Supporting evidence:");
            foreach (var item in primary.SupportingEvidence.Take(3))
                sb.AppendLine($"  · {TruncateAt(item, 120)}");
            sb.AppendLine();
        }

        if (candidates.Count > 1)
        {
            sb.AppendLine($"Other candidates ({candidates.Count - 1} additional):");
            foreach (var c in candidates.Skip(1).Take(2))
                sb.AppendLine($"  · {c.CauseTitle} ({c.ConfidenceLabel})");
            sb.AppendLine();
        }

        sb.Append("Open the Investigation page for full evidence chains and guided remediation workflows.");
        return sb.ToString().TrimEnd();
    }

    private static string BuildHelpText() =>
        "I can answer questions about your system using live telemetry data.\n\n" +
        "Try asking:\n" +
        "• \"Why is my PC slow?\" — performance analysis\n" +
        "• \"What's using my RAM?\" — memory breakdown\n" +
        "• \"What's wrong with my disk?\" — storage findings\n" +
        "• \"How's my CPU doing?\" — processor and thermal status\n" +
        "• \"Investigate root causes\" — evidence graph analysis\n" +
        "• \"What changed today?\" — timeline of recent events\n" +
        "• \"How's my PC?\" — overall system status\n\n" +
        "All answers are based on real data from this machine. " +
        "No information leaves this device.";

    // ── Message construction ───────────────────────────────────────────────────

    private static CompanionMessage BuildMessage(string text, CompanionMessageCategory category) =>
        new()
        {
            Id        = Guid.NewGuid().ToString("N")[..8],
            Role      = CompanionMessageRole.System,
            Text      = text,
            Timestamp = DateTimeOffset.Now,
            Category  = category,
        };

    private static CompanionMessage BuildErrorMessage(string text) =>
        new()
        {
            Id        = Guid.NewGuid().ToString("N")[..8],
            Role      = CompanionMessageRole.System,
            Text      = text,
            Timestamp = DateTimeOffset.Now,
            Category  = CompanionMessageCategory.Error,
        };

    // ── Formatting helpers ─────────────────────────────────────────────────────

    private static string FormatTimeAgo(DateTimeOffset ts)
    {
        var delta = DateTimeOffset.Now - ts;
        if (delta.TotalMinutes < 2)  return "just now";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalHours   < 24) return $"{(int)delta.TotalHours}h ago";
        return ts.LocalDateTime.ToString("HH:mm");
    }

    private static string TruncateAt(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..maxChars] + "…";

    // ── Intent enum ────────────────────────────────────────────────────────────

    private enum QueryIntent
    {
        Status,
        Performance,
        Memory,
        Disk,
        Cpu,
        Startup,
        Security,
        Timeline,
        Investigation,
        Help,
    }
}
