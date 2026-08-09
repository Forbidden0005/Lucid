using Lucid.Services.Interaction.Language;
using Lucid.Services.Timeline;

namespace Lucid.Services.Interaction.Narrative;

/// <summary>
/// Identifies related events in the timeline and builds brief correlation narratives.
///
/// Correlation narration answers:
///   "How were these events connected to each other?"
///
/// Examples:
///   - "A startup-congestion insight onset was followed by 2 startup-related actions."
///   - "A thermal alert preceded a performance degradation finding by 3 minutes."
///
/// This is heuristic — correlations are based on temporal proximity and event
/// type relationships. Lucid never claims causal certainty for correlations;
/// language is always hedged appropriately.
///
/// Thread-safe: stateless and pure.
/// </summary>
public static class EventCorrelationNarrator
{
    /// <summary>
    /// Maximum time window for two events to be considered "related" (5 minutes).
    /// </summary>
    private static readonly TimeSpan CorrelationWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Identifies events that are temporally and categorically related to
    /// <paramref name="anchor"/> and returns a prose correlation description.
    ///
    /// Returns null when no correlated events are found.
    /// </summary>
    public static string? BuildCorrelationNote(
        TimelineEvent             anchor,
        IReadOnlyList<TimelineEvent> allEvents)
    {
        if (allEvents.Count < 2) return null;

        // Find events within the correlation window.
        var related = allEvents
            .Where(e => e != anchor &&
                        Math.Abs((e.OccurredAt - anchor.OccurredAt).TotalSeconds) <=
                        CorrelationWindow.TotalSeconds)
            .OrderBy(e => e.OccurredAt)
            .ToList();

        if (related.Count == 0) return null;

        // Describe the relationship.
        var followingCount = related.Count(e => e.OccurredAt > anchor.OccurredAt);
        var precedingCount = related.Count - followingCount;

        var parts = new List<string>(2);

        if (precedingCount > 0)
            parts.Add($"Preceded by {precedingCount} related event{Plural(precedingCount)} " +
                      "within 5 minutes");

        if (followingCount > 0)
            parts.Add($"Followed by {followingCount} related event{Plural(followingCount)} " +
                      "within 5 minutes");

        var note = string.Join("; ", parts) + ".";
        note += " Temporal proximity may indicate a relationship, though causal links " +
                "require further investigation.";

        return CalmCommunicationFormatter.FormatStandard(note);
    }

    /// <summary>
    /// Returns a narrative chain description for a sequence of related events.
    /// Used in the Timeline page "What happened here?" detail panel.
    /// </summary>
    public static string BuildSequenceNarrative(IReadOnlyList<TimelineEvent> events)
    {
        if (events.Count == 0) return "No events to narrate.";
        if (events.Count == 1) return BuildSingleEventSummary(events[0]);

        var ordered = events.OrderBy(e => e.OccurredAt).ToList();
        var first   = ordered.First();
        var last    = ordered.Last();
        var span    = last.OccurredAt - first.OccurredAt;
        var spanStr = span.TotalMinutes < 1
            ? "within seconds"
            : span.TotalMinutes < 60
                ? $"over {(int)span.TotalMinutes} minutes"
                : $"over {span.TotalHours:F1} hours";

        var actionCount  = events.Count(e => e.Type == TimelineEventType.ActionExecuted);
        var insightCount = events.Count(e => e.Type == TimelineEventType.InsightOnset);

        var summary = $"{events.Count} events {spanStr}";
        if (insightCount > 0) summary += $", {insightCount} intelligence finding{Plural(insightCount)}";
        if (actionCount  > 0) summary += $", {actionCount} action{Plural(actionCount)} executed";

        return CalmCommunicationFormatter.FormatStandard(summary + ".");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildSingleEventSummary(TimelineEvent e) =>
        CalmCommunicationFormatter.FormatCompact(
            $"{DescribeEventType(e.Type)} — {e.Title}");

    private static string DescribeEventType(TimelineEventType type) => type switch
    {
        TimelineEventType.InsightOnset      => "Intelligence finding",
        TimelineEventType.InsightResolved   => "Finding resolved",
        TimelineEventType.ActionExecuted    => "Action executed",
        TimelineEventType.ActionRollback    => "Action rolled back",
        TimelineEventType.ForecastAlert     => "Forecast alert",
        TimelineEventType.ProcessAnomaly    => "Process anomaly",
        TimelineEventType.WorkflowCompleted => "Workflow completed",
        _                                   => "Operational event",
    };

    private static string Plural(int count) => count == 1 ? "" : "s";
}
