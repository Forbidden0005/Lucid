using Lucid.Services.Interaction.Language;
using Lucid.Services.Timeline;

namespace Lucid.Services.Interaction.Narrative;

/// <summary>
/// Builds a coherent operational timeline narrative from the raw event stream.
///
/// The CognitiveTimelineBuilder wraps <see cref="ITimelineAggregationService.Events"/>
/// and produces:
///   1. A narrative window summary ("In the last hour: 3 findings, 1 action")
///   2. Structured 15-minute bucket groups with captions for the Timeline page
///
/// This builder adds narrative cohesion to the raw event stream — it does NOT
/// replace the timeline service. Stateless and pure; call from any thread.
/// </summary>
public sealed class CognitiveTimelineBuilder
{
    private readonly ITimelineAggregationService _timeline;

    public CognitiveTimelineBuilder(ITimelineAggregationService timeline)
    {
        _timeline = timeline;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a narrative summary covering the last <paramref name="windowMinutes"/> minutes.
    /// Suitable for the Timeline page header.
    /// </summary>
    public string BuildWindowSummary(int windowMinutes = 60)
    {
        var cutoff  = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(windowMinutes);
        var events  = _timeline.Events
                               .Where(e => e.OccurredAt >= cutoff)
                               .OrderBy(e => e.OccurredAt)
                               .ToList();

        if (events.Count == 0)
            return $"No operational events recorded in the last {windowMinutes} minutes.";

        var insightEvts  = events.Count(e => e.Type == TimelineEventType.InsightOnset    ||
                                              e.Type == TimelineEventType.InsightResolved);
        var actionEvts   = events.Count(e => e.Type == TimelineEventType.ActionExecuted  ||
                                              e.Type == TimelineEventType.ActionRollback);
        var forecastEvts = events.Count(e => e.Type == TimelineEventType.ForecastAlert);
        var workflowEvts = events.Count(e => e.Type == TimelineEventType.WorkflowStarted ||
                                              e.Type == TimelineEventType.WorkflowCompleted);

        var parts = new List<string>(4);
        if (insightEvts  > 0) parts.Add($"{insightEvts} intelligence finding{Plural(insightEvts)}");
        if (actionEvts   > 0) parts.Add($"{actionEvts} action{Plural(actionEvts)} executed");
        if (forecastEvts > 0) parts.Add($"{forecastEvts} forecast update{Plural(forecastEvts)}");
        if (workflowEvts > 0) parts.Add($"{workflowEvts} workflow event{Plural(workflowEvts)}");

        var summary = parts.Count > 0
            ? $"In the last {windowMinutes} minutes: {string.Join(", ", parts)}."
            : $"{events.Count} events in the last {windowMinutes} minutes.";

        return CalmCommunicationFormatter.FormatStandard(summary);
    }

    /// <summary>
    /// Groups the most recent <paramref name="maxEvents"/> events into 15-minute
    /// narrative buckets, each with a caption.
    /// </summary>
    public IReadOnlyList<TimelineNarrativeGroup> BuildNarrativeGroups(int maxEvents = 100)
    {
        var events = _timeline.Events
                              .OrderByDescending(e => e.OccurredAt)
                              .Take(maxEvents)
                              .ToList();

        if (events.Count == 0) return [];

        return events
            .GroupBy(e =>
            {
                var t = e.OccurredAt;
                return new DateTimeOffset(
                    t.Year, t.Month, t.Day, t.Hour, (t.Minute / 15) * 15, 0, t.Offset);
            })
            .OrderByDescending(g => g.Key)
            .Select(g => new TimelineNarrativeGroup(
                WindowStart: g.Key,
                Caption:     BuildGroupCaption(g.Key, g.ToList()),
                Events:      g.OrderByDescending(e => e.OccurredAt).ToList().AsReadOnly()))
            .ToList()
            .AsReadOnly();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildGroupCaption(DateTimeOffset windowStart, List<TimelineEvent> events)
    {
        var diff = DateTimeOffset.UtcNow - windowStart;

        var timeLabel = diff.TotalMinutes < 2  ? "Just now"
                      : diff.TotalMinutes < 60 ? $"{(int)diff.TotalMinutes} min ago"
                      : diff.TotalHours   < 24 ? $"{(int)diff.TotalHours}h ago"
                      : windowStart.ToString("MMM d");

        var dominant = events
            .GroupBy(e => e.Type)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        var activity = dominant?.Key switch
        {
            TimelineEventType.InsightOnset       => "intelligence activity",
            TimelineEventType.InsightResolved    => "findings resolved",
            TimelineEventType.ActionExecuted     => "actions executed",
            TimelineEventType.ForecastAlert      => "forecast updates",
            TimelineEventType.WorkflowCompleted  => "workflows completed",
            TimelineEventType.ProcessAnomaly     => "process anomalies",
            TimelineEventType.NarrativeCheckpoint => "narrative checkpoint",
            _                                    => "operational events",
        };

        return $"{timeLabel} — {events.Count} {activity}";
    }

    private static string Plural(int count) => count == 1 ? "" : "s";
}

/// <summary>A time-windowed group of timeline events with a narrative caption.</summary>
public sealed record TimelineNarrativeGroup(
    DateTimeOffset               WindowStart,
    string                       Caption,
    IReadOnlyList<TimelineEvent> Events);
