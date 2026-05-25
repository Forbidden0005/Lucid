using System.Text;
using Lucid.Services.Companion;
using Lucid.Services.DesktopContext;
using Lucid.Services.Intelligence;
using Lucid.Services.Narrative;
using Lucid.Services.Reasoning;
using Lucid.Services.Timeline;

namespace Lucid.Services.Conversation;

/// <summary>
/// Composes structured <see cref="OperationalResponse"/> objects from live
/// platform service data, guided by the resolved <see cref="IntentMatch"/>.
///
/// Every response is:
///   • Grounded in specific platform service observations
///   • Confidence-bounded — never claims more certainty than the data supports
///   • Evidence-tagged — exposes which sources were consulted
///   • Uncertainty-aware — calls out gaps when data is insufficient
///   • Probabilistic — uses "appears", "may be", "suggests" language throughout
///
/// Language rules (enforced in every compose method):
///   GOOD: "CPU utilization is currently elevated alongside high background activity."
///   GOOD: "Memory pressure appears to have increased after startup apps initialized."
///   BAD:  "Your PC is dangerously overloaded." (fear-based, absolute)
///   BAD:  "This definitely caused the issue." (false certainty)
/// </summary>
public sealed class OperationalResponseComposer
{
    // ── Dependencies ───────────────────────────────────────────────────────────

    private readonly ISystemInsightEngine        _insights;
    private readonly IOperationalNarrativeEngine _narrative;
    private readonly ITimelineAggregationService _timeline;
    private readonly IOperationalEvidenceGraph   _evidenceGraph;
    private readonly IRootCauseAnalysisEngine    _rootCauseEngine;
    private readonly IDesktopContextService      _desktopContext;
    private readonly ReplayConversationBridge    _replayBridge;
    private readonly WorkflowConversationBridge  _workflowBridge;
    private readonly EvidenceRetrievalPlanner    _planner;

    public OperationalResponseComposer(
        ISystemInsightEngine        insights,
        IOperationalNarrativeEngine narrative,
        ITimelineAggregationService timeline,
        IOperationalEvidenceGraph   evidenceGraph,
        IRootCauseAnalysisEngine    rootCauseEngine,
        IDesktopContextService      desktopContext,
        ReplayConversationBridge    replayBridge,
        WorkflowConversationBridge  workflowBridge,
        EvidenceRetrievalPlanner    planner)
    {
        _insights        = insights;
        _narrative       = narrative;
        _timeline        = timeline;
        _evidenceGraph   = evidenceGraph;
        _rootCauseEngine = rootCauseEngine;
        _desktopContext  = desktopContext;
        _replayBridge    = replayBridge;
        _workflowBridge  = workflowBridge;
        _planner         = planner;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Composes a full <see cref="OperationalResponse"/> for the given intent.
    /// Async only for investigation (evidence graph build); all other paths are synchronous.
    /// </summary>
    public async Task<OperationalResponse> ComposeAsync(
        IntentMatch match, OperationalPrompt prompt, CancellationToken ct)
    {
        var sources  = _planner.Plan(match.Intent);
        var actions  = _workflowBridge.GetSuggestedActions(match.Intent);

        try
        {
            var (text, evidence, confidence, uncertainty, category) =
                await ComposeCoreAsync(match, prompt, ct).ConfigureAwait(false);

            return new OperationalResponse
            {
                Text             = text,
                Intent           = match,
                Confidence       = new ResponseConfidence
                                   {
                                       Percent = confidence,
                                       Basis   = EvidenceRetrievalPlanner.FormatSourceList(sources),
                                   },
                Evidence         = evidence,
                SuggestedActions = actions,
                UncertaintyNote  = uncertainty,
                Category         = category,
                EvidenceSources  = sources,
            };
        }
        catch (OperationCanceledException)
        {
            return ErrorResponse(match, "Analysis was cancelled.", sources);
        }
        catch (Exception ex)
        {
            return ErrorResponse(match, $"Could not complete analysis: {ex.Message}", sources);
        }
    }

    // ── Core dispatch ──────────────────────────────────────────────────────────

    private async Task<(string text, IReadOnlyList<ConversationEvidenceItem> evidence,
                        int confidence, string? uncertainty, CompanionMessageCategory category)>
        ComposeCoreAsync(IntentMatch match, OperationalPrompt prompt, CancellationToken ct)
    {
        return match.Intent switch
        {
            ConversationIntent.Help             => (HelpText(),                null_ev, 99,  null, CompanionMessageCategory.Answer),
            ConversationIntent.Greeting         => (GreetingText(),            null_ev, 99,  null, CompanionMessageCategory.Welcome),
            ConversationIntent.WhyIsSlow        => ComposePerformanceResponse(),
            ConversationIntent.WhyIsHot         => ComposeThermalResponse(),
            ConversationIntent.WhyIsDiskFull    => ComposeDiskResponse(),
            ConversationIntent.WhyIsMemoryHigh  => ComposeMemoryResponse(),
            ConversationIntent.OpenRepairs or
            ConversationIntent.StartGuidedWorkflow
                                                => ComposeRepairsNavResponse(),
            ConversationIntent.OpenStorage or
            ConversationIntent.FindRecentDownloads
                                                => ComposeStorageNavResponse(prompt),
            ConversationIntent.OpenTimeline or
            ConversationIntent.ReviewTimeline   => ComposeTimelineNavResponse(),
            ConversationIntent.OpenInvestigation or
            ConversationIntent.InvestigateProblem
                => await ComposeInvestigationResponseAsync(ct),
            ConversationIntent.WhyDidSomethingChange or
            ConversationIntent.CompareChanges   => ComposeChangesResponse(prompt),
            ConversationIntent.OpenReplay       => ComposeReplayNavResponse(prompt),
            ConversationIntent.OpenHistorical   => ComposeHistoricalNavResponse(),
            ConversationIntent.ExplainRecommendation
                                                => ComposeExplainRecommendationResponse(),
            ConversationIntent.ReviewClipboardFiles
                                                => ComposeClipboardResponse(prompt),
            ConversationIntent.ExplainCurrentContext
                                                => ComposeContextResponse(prompt),
            // ── Phase 17D: automation launch responses ────────────────────────
            ConversationIntent.LaunchTaskManager     => ComposeAutomationLaunchResponse(
                "Task Manager",
                "I'll open Task Manager so you can see real-time CPU, memory, disk, and network usage by process.",
                "automation.launch.task-manager", "task-manager"),
            ConversationIntent.LaunchDeviceManager   => ComposeAutomationLaunchResponse(
                "Device Manager",
                "I'll open Device Manager so you can check hardware devices and driver status. Look for yellow warning triangles.",
                "automation.launch.device-manager", "device-manager"),
            ConversationIntent.LaunchEventViewer     => ComposeAutomationLaunchResponse(
                "Event Viewer",
                "I'll open Event Viewer so you can review Windows system and application error logs. Navigate to Windows Logs → System.",
                "automation.launch.event-viewer", "event-viewer"),
            ConversationIntent.LaunchStorageSettings => ComposeAutomationLaunchResponse(
                "Storage Settings",
                "I'll open the Storage settings page to show you a breakdown of disk usage by category.",
                "automation.launch.storage-settings", "storage-settings"),
            ConversationIntent.LaunchStartupApps     => ComposeAutomationLaunchResponse(
                "Startup Apps",
                "I'll open the Startup Apps settings so you can see which apps run at login and their impact on boot time.",
                "automation.launch.startup-apps", "startup-apps"),
            ConversationIntent.LaunchWindowsSecurity => ComposeAutomationLaunchResponse(
                "Windows Security",
                "I'll open Windows Security so you can review your antivirus, firewall, and account protection status.",
                "automation.launch.windows-security", "windows-security"),
            ConversationIntent.OpenDownloadsFolder   => ComposeAutomationLaunchResponse(
                "Downloads folder",
                "I'll open your Downloads folder in File Explorer so you can review its contents.",
                "automation.explorer.downloads", "downloads"),
            ConversationIntent.OpenDocumentsFolder   => ComposeAutomationLaunchResponse(
                "Documents folder",
                "I'll open your Documents folder in File Explorer.",
                "automation.explorer.documents", "documents"),
            ConversationIntent.LaunchNetworkSettings => ComposeAutomationLaunchResponse(
                "Network Settings",
                "I'll open Network Settings so you can review your connection status or run the network troubleshooter.",
                "automation.launch.network-settings", "network-settings"),
            ConversationIntent.LaunchWindowsUpdate   => ComposeAutomationLaunchResponse(
                "Windows Update",
                "I'll open Windows Update so you can check for and install available updates.",
                "automation.launch.windows-update", "windows-update"),
            _                                   => ComposeStatusResponse(),
        };
    }

    // Sentinel for no evidence
    private static readonly IReadOnlyList<ConversationEvidenceItem> null_ev = [];

    // ── Compose methods ────────────────────────────────────────────────────────

    private (string, IReadOnlyList<ConversationEvidenceItem>, int, string?, CompanionMessageCategory)
        ComposeStatusResponse()
    {
        var n = _narrative.CurrentNarrative;
        if (n is null)
            return ("Telemetry is still initializing — check back in a few seconds once " +
                    "the monitoring engine has collected its first readings.",
                    null_ev, 40, "Telemetry initializing; low data volume", CompanionMessageCategory.Answer);

        var sb = new StringBuilder();
        sb.AppendLine(n.Headline);
        sb.AppendLine();
        sb.Append(n.StatusParagraph);

        var warnings = _insights.CurrentInsights
            .Where(i => i.Severity >= InsightSeverity.Warning).Take(3).ToList();
        if (warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append($"{warnings.Count} active finding{(warnings.Count > 1 ? "s" : "")}: ");
            sb.Append(string.Join("; ", warnings.Select(w => w.Title.ToLowerInvariant())));
            sb.Append('.');
        }

        var ev = new List<ConversationEvidenceItem>
        {
            new() { Source = "Narrative engine", Summary = n.Headline },
        };
        if (warnings.Count > 0)
            ev.Add(new() { Source = "Insight engine", Summary = $"{warnings.Count} active warning(s)" });

        return (sb.ToString().TrimEnd(), ev, 80, null, CompanionMessageCategory.Answer);
    }

    private (string, IReadOnlyList<ConversationEvidenceItem>, int, string?, CompanionMessageCategory)
        ComposePerformanceResponse()
    {
        var insights = _insights.CurrentInsights
            .Where(i => i.Severity >= InsightSeverity.Recommendation)
            .OrderByDescending(i => i.Severity).Take(5).ToList();

        if (insights.Count == 0)
        {
            var n = _narrative.CurrentNarrative;
            var text = n is not null
                ? $"Performance appears stable. {n.StatusParagraph}"
                : "No active performance findings. Your system appears to be operating " +
                  "within expected ranges for its established baseline.";
            return (text, null_ev, 75, null, CompanionMessageCategory.Answer);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"{insights.Count} active finding{(insights.Count > 1 ? "s" : "")} " +
                      "that may be contributing to the slowdown:");
        sb.AppendLine();
        foreach (var i in insights)
        {
            sb.AppendLine($"• {i.Title} ({i.ConfidencePercent}% confidence)");
            sb.Append($"  {i.Detail}");
            if (i.ActionHint is not null) sb.Append($" → {i.ActionHint}");
            sb.AppendLine();
        }
        sb.Append("Open the Investigation page for a full causal analysis.");

        var ev = new List<ConversationEvidenceItem>
        {
            new() { Source = "Insight engine",
                    Summary = $"{insights.Count} finding(s): " +
                              string.Join(", ", insights.Take(2).Select(i => i.Title)) },
        };
        return (sb.ToString().TrimEnd(), ev, 82, null, CompanionMessageCategory.Warning);
    }

    private (string, IReadOnlyList<ConversationEvidenceItem>, int, string?, CompanionMessageCategory)
        ComposeThermalResponse()
    {
        var thermalInsights = _insights.CurrentInsights
            .Where(i => i.Id.Contains("thermal", StringComparison.OrdinalIgnoreCase)
                     || i.Id.Contains("temperature", StringComparison.OrdinalIgnoreCase)
                     || i.Title.Contains("Temperature", StringComparison.OrdinalIgnoreCase)
                     || i.Title.Contains("Thermal", StringComparison.OrdinalIgnoreCase)
                     || i.Title.Contains("Hot", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Severity).ToList();

        if (thermalInsights.Count == 0)
            return ("No active thermal findings. CPU temperature appears to be within " +
                    "expected ranges for this machine's baseline profile.",
                    null_ev, 72, null, CompanionMessageCategory.Answer);

        var sb = new StringBuilder();
        sb.AppendLine($"Thermal analysis — {thermalInsights.Count} " +
                      $"finding{(thermalInsights.Count > 1 ? "s" : "")}:");
        sb.AppendLine();
        foreach (var i in thermalInsights)
        {
            sb.AppendLine($"• {i.Title}");
            sb.Append($"  {i.Detail}");
            if (i.ActionHint is not null) sb.Append($" → {i.ActionHint}");
            sb.AppendLine();
        }
        sb.Append("Review the Processes page to identify thermally intensive workloads.");

        var ev = new List<ConversationEvidenceItem>
        {
            new() { Source = "Insight engine", Summary = $"{thermalInsights.Count} thermal finding(s)" },
        };
        return (sb.ToString().TrimEnd(), ev, 85, null, CompanionMessageCategory.Warning);
    }

    private (string, IReadOnlyList<ConversationEvidenceItem>, int, string?, CompanionMessageCategory)
        ComposeDiskResponse()
    {
        var diskInsights = _insights.CurrentInsights
            .Where(i => i.Id.Contains("disk", StringComparison.OrdinalIgnoreCase)
                     || i.Id.Contains("storage", StringComparison.OrdinalIgnoreCase)
                     || i.Title.Contains("Disk", StringComparison.OrdinalIgnoreCase)
                     || i.Title.Contains("Storage", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Severity).ToList();

        if (diskInsights.Count == 0)
            return ("No active disk or storage findings. Disk space and I/O throughput " +
                    "appear to be within expected ranges based on this machine's baseline.",
                    null_ev, 72, null, CompanionMessageCategory.Answer);

        var sb = new StringBuilder();
        sb.AppendLine($"Disk analysis — {diskInsights.Count} " +
                      $"finding{(diskInsights.Count > 1 ? "s" : "")}:");
        sb.AppendLine();
        foreach (var i in diskInsights)
        {
            sb.AppendLine($"• {i.Title}");
            sb.Append($"  {i.Detail}");
            if (i.ActionHint is not null) sb.Append($" → {i.ActionHint}");
            sb.AppendLine();
        }
        sb.Append("Open the Storage page to identify large files and duplicate groups.");

        var ev = new List<ConversationEvidenceItem>
        {
            new() { Source = "Insight engine", Summary = $"{diskInsights.Count} disk finding(s)" },
        };
        return (sb.ToString().TrimEnd(), ev, 83, null, CompanionMessageCategory.Warning);
    }

    private (string, IReadOnlyList<ConversationEvidenceItem>, int, string?, CompanionMessageCategory)
        ComposeMemoryResponse()
    {
        var ramInsights = _insights.CurrentInsights
            .Where(i => i.Id.Contains("ram", StringComparison.OrdinalIgnoreCase)
                     || i.Id.Contains("memory", StringComparison.OrdinalIgnoreCase)
                     || i.Title.Contains("RAM", StringComparison.OrdinalIgnoreCase)
                     || i.Title.Contains("Memory", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Severity).ToList();

        if (ramInsights.Count == 0)
            return ("No active memory pressure findings. RAM usage appears to be within " +
                    "expected ranges for this machine's baseline profile.",
                    null_ev, 72, null, CompanionMessageCategory.Answer);

        var sb = new StringBuilder();
        sb.AppendLine($"Memory analysis — {ramInsights.Count} " +
                      $"finding{(ramInsights.Count > 1 ? "s" : "")}:");
        sb.AppendLine();
        foreach (var i in ramInsights)
        {
            sb.AppendLine($"• {i.Title}");
            sb.Append($"  {i.Detail}");
            if (i.ActionHint is not null) sb.Append($" → {i.ActionHint}");
            sb.AppendLine();
        }
        sb.Append("Open the Processes page to identify which processes are consuming RAM.");

        var ev = new List<ConversationEvidenceItem>
        {
            new() { Source = "Insight engine", Summary = $"{ramInsights.Count} memory finding(s)" },
        };
        return (sb.ToString().TrimEnd(), ev, 83, null, CompanionMessageCategory.Warning);
    }

    private (string, IReadOnlyList<ConversationEvidenceItem>, int, string?, CompanionMessageCategory)
        ComposeRepairsNavResponse()
    {
        var repairableInsights = _insights.CurrentInsights
            .Where(i => i.Severity >= InsightSeverity.Warning).Take(3).ToList();

        var text = repairableInsights.Count > 0
            ? $"The Repairs page contains guided workflows for {repairableInsights.Count} " +
              $"active finding{(repairableInsights.Count == 1 ? "" : "s")}. " +
              "All workflows include a preview step and full rollback support.\n\n" +
              string.Join("\n", repairableInsights.Select(i => $"• {i.Title}"))
            : "The Repairs page contains all available guided cleanup and repair workflows. " +
              "Each workflow explains its steps and expected outcome before anything runs.";

        return (text, null_ev, 88, null, CompanionMessageCategory.Action);
    }

    private (string, IReadOnlyList<ConversationEvidenceItem>, int, string?, CompanionMessageCategory)
        ComposeStorageNavResponse(OperationalPrompt prompt)
    {
        string focus = string.IsNullOrEmpty(prompt.DesktopContextFocus)
            ? "The Storage page"
            : $"Since you appear to be working in {prompt.DesktopContextFocus}, the Storage page";

        return (focus + " provides analysis of large files, duplicate groups, " +
                "and cleanup opportunities. All operations are preview-only until you confirm them.",
                null_ev, 88, null, CompanionMessageCategory.Action);
    }

    private (string, IReadOnlyList<ConversationEvidenceItem>, int, string?, CompanionMessageCategory)
        ComposeTimelineNavResponse()
    {
        var cutoff = DateTimeOffset.Now.AddHours(-1);
        var recentCount = _timeline.Events.Count(e => e.OccurredAt >= cutoff);

        var text = recentCount > 0
            ? $"The Timeline page shows {recentCount} event{(recentCount == 1 ? "" : "s")} " +
              "from the last hour, grouped by time period. Supports filtering by warnings, " +
              "actions, insights, and session events."
            : "The Timeline page shows the operational event log grouped by time period. " +
              "Events populate as insights fire, actions execute, and session state changes.";

        var ev = new List<ConversationEvidenceItem>
        {
            new() { Source = "Timeline", Summary = $"{recentCount} events in last hour" },
        };
        return (text, ev, 90, null, CompanionMessageCategory.Insight);
    }

    private async Task<(string, IReadOnlyList<ConversationEvidenceItem>, int, string?, CompanionMessageCategory)>
        ComposeInvestigationResponseAsync(CancellationToken ct)
    {
        var graph = _evidenceGraph.CurrentGraph
            ?? await _evidenceGraph.BuildGraphAsync(ct).ConfigureAwait(false);

        var candidates = _rootCauseEngine.AnalyzeRootCauses(graph, maxCandidates: 3);

        if (candidates.Count == 0)
            return ("The evidence graph does not contain enough signal to isolate a primary " +
                    "root cause at this time. Ensure the monitoring engine has been running " +
                    "for at least a few minutes so insights, anomalies, and drifts accumulate. " +
                    "Open the Investigation page for the live evidence chain.",
                    null_ev, 45,
                    "Insufficient evidence signal for high-confidence root cause isolation.",
                    CompanionMessageCategory.Insight);

        var primary = candidates[0];
        var sb = new StringBuilder();
        sb.AppendLine($"Root cause analysis — {graph.Nodes.Count} evidence " +
                      $"node{(graph.Nodes.Count == 1 ? "" : "s")}, " +
                      $"{graph.Edges.Count} causal link{(graph.Edges.Count == 1 ? "" : "s")}:");
        sb.AppendLine();
        sb.AppendLine($"Primary candidate ({primary.ConfidenceLabel}):");
        sb.AppendLine($"• {primary.CauseTitle}");
        sb.AppendLine($"  {primary.Explanation}");

        if (primary.SupportingEvidence.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Supporting evidence:");
            foreach (var item in primary.SupportingEvidence.Take(3))
                sb.AppendLine($"  · {TruncateAt(item, 120)}");
        }

        if (candidates.Count > 1)
        {
            sb.AppendLine();
            sb.AppendLine($"Additional candidates ({candidates.Count - 1}):");
            foreach (var c in candidates.Skip(1).Take(2))
                sb.AppendLine($"  · {c.CauseTitle} ({c.ConfidenceLabel})");
        }

        sb.Append("\nOpen the Investigation page for full evidence chains and guided workflows.");

        var ev = new List<ConversationEvidenceItem>
        {
            new() { Source = "Evidence graph",
                    Summary = $"{graph.Nodes.Count} nodes, {graph.Edges.Count} links" },
            new() { Source = "Root cause engine",
                    Summary = $"{candidates.Count} candidate(s) identified" },
        };

        return (sb.ToString().TrimEnd(), ev, 78, null, CompanionMessageCategory.Insight);
    }

    private (string, IReadOnlyList<ConversationEvidenceItem>, int, string?, CompanionMessageCategory)
        ComposeChangesResponse(OperationalPrompt prompt)
    {
        var lookback = TimeSpan.FromHours(6);
        var (text, evidence) = _replayBridge.SummarizeRecentChanges(lookback);
        return (text, evidence, 80, null, CompanionMessageCategory.Insight);
    }

    private (string, IReadOnlyList<ConversationEvidenceItem>, int, string?, CompanionMessageCategory)
        ComposeReplayNavResponse(OperationalPrompt prompt)
    {
        var (summaryText, evidence) = _replayBridge.SummarizeRecentChanges(TimeSpan.FromHours(24));
        var text = summaryText + "\n\nOpen the Replay page for a detailed session comparison.";
        return (text, evidence, 82, null, CompanionMessageCategory.Insight);
    }

    private (string, IReadOnlyList<ConversationEvidenceItem>, int, string?, CompanionMessageCategory)
        ComposeHistoricalNavResponse()
    {
        return ("The Historical Analysis page shows long-term health trends, recurring patterns, " +
                "and metric trajectories computed from the operational database. " +
                "All analysis is local-only and deterministic.",
                null_ev, 90, null, CompanionMessageCategory.Insight);
    }

    private (string, IReadOnlyList<ConversationEvidenceItem>, int, string?, CompanionMessageCategory)
        ComposeExplainRecommendationResponse()
    {
        var topInsights = _insights.CurrentInsights
            .Where(i => i.Severity >= InsightSeverity.Recommendation)
            .OrderByDescending(i => i.Severity).Take(3).ToList();

        if (topInsights.Count == 0)
            return ("No active recommendations at this time. Recommendations appear when " +
                    "the intelligence engine detects patterns worth addressing.",
                    null_ev, 80, null, CompanionMessageCategory.Answer);

        var sb = new StringBuilder();
        sb.AppendLine($"Current recommendations ({topInsights.Count}):");
        sb.AppendLine();
        foreach (var i in topInsights)
        {
            sb.AppendLine($"• {i.Title} — {i.ConfidencePercent}% confidence");
            sb.AppendLine($"  Why recommended: {i.Detail}");
            if (i.ActionHint is not null) sb.AppendLine($"  Suggested action: {i.ActionHint}");
            sb.AppendLine();
        }
        sb.Append("Open the Insights page to see all recommendations with full evidence context.");

        var ev = new List<ConversationEvidenceItem>
        {
            new() { Source = "Insight engine", Summary = $"{topInsights.Count} recommendation(s)" },
        };
        return (sb.ToString().TrimEnd(), ev, 85, null, CompanionMessageCategory.Insight);
    }

    private (string, IReadOnlyList<ConversationEvidenceItem>, int, string?, CompanionMessageCategory)
        ComposeClipboardResponse(OperationalPrompt prompt)
    {
        var snap = _desktopContext.CurrentSnapshot;
        var clipboard = snap?.ClipboardContext;

        if (clipboard is null || !clipboard.HasFiles)
            return ("No files detected in the clipboard at this time. " +
                    "If you have files copied, try again — the clipboard is checked periodically.",
                    null_ev, 60, "Clipboard state is checked at 1750ms intervals.", CompanionMessageCategory.Answer);

        var sb = new StringBuilder();
        sb.AppendLine($"Clipboard contains {clipboard.FileCount} file{(clipboard.FileCount == 1 ? "" : "s")}.");
        sb.AppendLine();
        sb.AppendLine("You can use the Storage page to analyze file sizes and find duplicates, " +
                      "or use the Cleanup workflows to organize your files.");

        var ev = new List<ConversationEvidenceItem>
        {
            new() { Source = "Desktop context", Summary = $"{clipboard.FileCount} file(s) in clipboard" },
        };
        return (sb.ToString().TrimEnd(), ev, 78, null, CompanionMessageCategory.Answer);
    }

    private (string, IReadOnlyList<ConversationEvidenceItem>, int, string?, CompanionMessageCategory)
        ComposeContextResponse(OperationalPrompt prompt)
    {
        var snap = _desktopContext.CurrentSnapshot;

        if (snap is null || string.IsNullOrEmpty(snap.CurrentOperationalFocus))
            return ("Desktop context is not available or no significant workflow has been detected. " +
                    "Context awareness observes the foreground window and File Explorer path only.",
                    null_ev, 50,
                    "Context requires active foreground window observation.",
                    CompanionMessageCategory.Answer);

        var sb = new StringBuilder();
        sb.AppendLine($"Current operational focus: {snap.CurrentOperationalFocus}");
        sb.AppendLine();

        if (snap.DetectedWorkflowHints?.Count > 0)
        {
            sb.AppendLine("Context hints:");
            foreach (var hint in snap.DetectedWorkflowHints.Take(3))
                sb.AppendLine($"  · {hint}");
        }

        sb.AppendLine();
        sb.Append("This context is observation-only — no screenshots, no keylogging. " +
                  "Only the active window title and File Explorer path are used.");

        var ev = new List<ConversationEvidenceItem>
        {
            new() { Source = "Desktop context", Summary = snap.CurrentOperationalFocus },
        };
        return (sb.ToString().TrimEnd(), ev, 75, null, CompanionMessageCategory.Answer);
    }

    // ── Phase 17D: automation launch response ─────────────────────────────────

    private (string, IReadOnlyList<ConversationEvidenceItem>, int, string?, CompanionMessageCategory)
        ComposeAutomationLaunchResponse(
            string targetLabel, string narration, string actionId, string launchTarget)
    {
        var text = narration +
                   "\n\nTap the action below to proceed. You can cancel at any time.";

        return (text, null_ev, 95, null, CompanionMessageCategory.Action);
    }

    // ── Static text ────────────────────────────────────────────────────────────

    private static string HelpText() =>
        "I can answer operational questions about your system using live data.\n\n" +
        "Try asking:\n" +
        "• \"Why is my PC slow?\" — performance analysis\n" +
        "• \"What's using my RAM?\" — memory breakdown\n" +
        "• \"Why is my disk full?\" — storage findings\n" +
        "• \"Why is it hot?\" — thermal analysis\n" +
        "• \"Investigate root causes\" — evidence graph analysis\n" +
        "• \"What changed today?\" — change history\n" +
        "• \"How's my PC?\" — overall status\n\n" +
        "All answers are grounded in real data from this machine. " +
        "No information leaves this device.";

    private static string GreetingText() =>
        "Hello — I'm the Operational Companion. I can help you understand " +
        "what your system is doing, surface insights from live telemetry, " +
        "and guide you to the right tools.\n\n" +
        "Ask me anything about your system, or tap a quick action below.";

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static OperationalResponse ErrorResponse(
        IntentMatch match, string message, IReadOnlyList<EvidenceSource> sources) =>
        new()
        {
            Text       = message,
            Intent     = match,
            Confidence = new ResponseConfidence { Percent = 0, Basis = "Error" },
            Category   = CompanionMessageCategory.Error,
            EvidenceSources = sources,
        };

    private static string TruncateAt(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
