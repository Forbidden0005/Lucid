using System.Text;
using ExplainMyPC.Services.Companion;
using ExplainMyPC.Services.Timeline;

namespace ExplainMyPC.Services.Conversation;

/// <summary>
/// Exposes the replay and change-history system conversationally.
///
/// Uses the timeline event stream (always populated, synchronous) to answer
/// "what changed?" questions without triggering expensive replay snapshot
/// comparisons for the quick conversational response path.
///
/// For intents that explicitly request deep delta analysis
/// (<see cref="ConversationIntent.CompareChanges"/>), the bridge summarizes
/// the most operationally significant timeline events in the requested window.
///
/// Design constraints:
///   • Synchronous — no async, reads cached timeline state only.
///   • Observation only — never triggers replay, scans, or mutations.
///   • Uses probabilistic, non-alarmist language throughout.
/// </summary>
public sealed class ReplayConversationBridge
{
    private readonly ITimelineAggregationService _timeline;

    public ReplayConversationBridge(ITimelineAggregationService timeline)
    {
        _timeline = timeline;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Summarizes recent changes from the timeline event stream.
    /// Returns a plain text narrative; caller is responsible for wrapping
    /// it in an <see cref="OperationalResponse"/>.
    /// </summary>
    public (string Text, IReadOnlyList<ConversationEvidenceItem> Evidence)
        SummarizeRecentChanges(TimeSpan lookback)
    {
        var cutoff = DateTimeOffset.Now - lookback;
        var events = _timeline.Events
            .Where(e => e.OccurredAt >= cutoff)
            .OrderByDescending(e => e.OccurredAt)
            .ToList();

        if (events.Count == 0)
            return (NoEventsText(lookback), []);

        var sb = new StringBuilder();
        var evidence = new List<ConversationEvidenceItem>();

        // Categorize events
        var actionEvents   = events.Where(e =>
            e.Type is TimelineEventType.ActionExecuted or
                           TimelineEventType.ActionRollback).ToList();
        var insightEvents  = events.Where(e =>
            e.Type is TimelineEventType.InsightOnset or
                           TimelineEventType.InsightResolved).ToList();
        var sessionEvents  = events.Where(e =>
            e.Type is TimelineEventType.SystemBoot or
                           TimelineEventType.SessionStart or
                           TimelineEventType.WakeFromSleep).ToList();

        sb.AppendLine($"Last {FormatLookback(lookback)} — " +
                      $"{events.Count} timeline event{(events.Count == 1 ? "" : "s")} observed:");
        sb.AppendLine();

        // Actions taken
        if (actionEvents.Count > 0)
        {
            sb.AppendLine($"Actions executed ({actionEvents.Count}):");
            foreach (var e in actionEvents.Take(4))
                sb.AppendLine($"  • {e.Title} — {FormatTimeAgo(e.OccurredAt)}");
            sb.AppendLine();

            evidence.Add(new ConversationEvidenceItem
            {
                Source  = "Operation history",
                Summary = $"{actionEvents.Count} action{(actionEvents.Count == 1 ? "" : "s")} executed",
            });
        }

        // Insight changes
        var newInsights = insightEvents
            .Where(e => e.Type == TimelineEventType.InsightOnset).ToList();
        var clearedInsights = insightEvents
            .Where(e => e.Type == TimelineEventType.InsightResolved).ToList();

        if (newInsights.Count > 0)
        {
            sb.AppendLine($"New findings detected ({newInsights.Count}):");
            foreach (var e in newInsights.Take(4))
                sb.AppendLine($"  • {e.Title}");
            sb.AppendLine();

            evidence.Add(new ConversationEvidenceItem
            {
                Source  = "Insight engine",
                Summary = $"{newInsights.Count} new finding{(newInsights.Count == 1 ? "" : "s")}",
            });
        }

        if (clearedInsights.Count > 0)
        {
            sb.AppendLine($"Resolved findings ({clearedInsights.Count}):");
            foreach (var e in clearedInsights.Take(3))
                sb.AppendLine($"  • {e.Title}");
            sb.AppendLine();

            evidence.Add(new ConversationEvidenceItem
            {
                Source  = "Insight engine",
                Summary = $"{clearedInsights.Count} finding{(clearedInsights.Count == 1 ? "" : "s")} resolved",
            });
        }

        // Session events
        if (sessionEvents.Count > 0)
        {
            sb.AppendLine($"Session events ({sessionEvents.Count}):");
            foreach (var e in sessionEvents.Take(2))
                sb.AppendLine($"  • {e.Title} — {FormatTimeAgo(e.OccurredAt)}");
            sb.AppendLine();
        }

        // Recent most-significant events if nothing else filled the response
        if (actionEvents.Count == 0 && newInsights.Count == 0)
        {
            sb.AppendLine("Most recent events:");
            foreach (var e in events.Take(5))
                sb.AppendLine($"  • [{FormatTimeAgo(e.OccurredAt)}] {e.Title}");
        }

        sb.Append("Open the Replay or Timeline pages for a complete operational history.");

        evidence.Add(new ConversationEvidenceItem
        {
            Source  = "Timeline",
            Summary = $"{events.Count} total events in window",
        });

        return (sb.ToString().TrimEnd(), evidence);
    }

    // ── Formatting helpers ─────────────────────────────────────────────────────

    private static string FormatLookback(TimeSpan ts)
    {
        if (ts.TotalMinutes <= 15) return "15 minutes";
        if (ts.TotalHours   <= 1)  return "1 hour";
        if (ts.TotalHours   <= 6)  return "6 hours";
        if (ts.TotalHours   <= 24) return "24 hours";
        return $"{(int)ts.TotalDays} day{((int)ts.TotalDays == 1 ? "" : "s")}";
    }

    private static string FormatTimeAgo(DateTimeOffset ts)
    {
        var delta = DateTimeOffset.Now - ts;
        if (delta.TotalMinutes < 2)  return "just now";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalHours   < 24) return $"{(int)delta.TotalHours}h ago";
        return ts.LocalDateTime.ToString("HH:mm");
    }

    private static string NoEventsText(TimeSpan lookback) =>
        $"No timeline events recorded in the last {FormatLookback(lookback)}. " +
        "The timeline populates as insights fire, actions execute, and session " +
        "transitions occur. Check back after the monitoring engine has been " +
        "running for a few minutes.";
}
