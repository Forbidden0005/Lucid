using ExplainMyPC.Services.Timeline;

namespace ExplainMyPC.Services.Replay;

/// <summary>
/// Builds causal chains from the timeline event stream.
///
/// A causal chain answers: "How did this condition develop?"
/// It traces the sequence of signals that led from initial warning to the
/// current state — correlating telemetry shifts, insight onsets, process
/// activity, and any remediations taken.
///
/// Causal confidence scoring:
///   Events separated by ≤ 2 minutes → 80 % base confidence
///   Events separated by ≤ 5 minutes → 70 % base confidence
///   Events separated by ≤ 10 minutes → 60 % base confidence
///   Telemetry shift with measurable gradient → +10 % confidence
///   Action followed by insight resolution → +15 % confidence
///
/// All output uses confidence-aware language per the project security doctrine.
/// </summary>
internal static class CausalChainAnalyzer
{
    private static readonly TimeSpan StrongCorrelationWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ModerateCorrelationWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan WeakCorrelationWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ActionEffectWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Builds a causal chain for all significant events within the given time range.
    /// </summary>
    public static CausalChain Build(
        IReadOnlyList<TimelineEvent>            events,         // already oldest-first
        IReadOnlyList<HistoricalTelemetryPoint> telemetrySeries,
        DateTimeOffset                          fromTime,
        DateTimeOffset                          toTime)
    {
        // Filter to the time range, ensure oldest-first order
        var rangeEvents = events
            .Where(e => e.OccurredAt >= fromTime && e.OccurredAt <= toTime)
            .OrderBy(e => e.OccurredAt)
            .ToList();

        if (rangeEvents.Count == 0)
            return EmptyChain(fromTime, toTime);

        var links = new List<CausalChainLink>();

        // ── Step 1: Look for telemetry shift before the first significant event ─
        var firstSignificant = rangeEvents.FirstOrDefault(e =>
            e.Type is TimelineEventType.InsightOnset
                   or TimelineEventType.ForecastAlert
                   or TimelineEventType.SynthesisCorrelated);

        if (firstSignificant is not null && telemetrySeries.Count > 0)
        {
            var shiftLink = DetectTelemetryShift(
                telemetrySeries,
                fromTime,
                firstSignificant.OccurredAt);

            if (shiftLink is not null)
                links.Add(shiftLink);
        }

        // ── Step 2: Map each timeline event to a causal link ─────────────────
        TimelineEvent? previousEvent = null;
        foreach (var ev in rangeEvents)
        {
            var link = BuildLinkForEvent(ev, previousEvent);
            if (link is not null)
            {
                links.Add(link);
                previousEvent = ev;
            }
        }

        // ── Step 3: Check whether any action was followed by stabilization ───
        AugmentWithStabilizationLinks(links, rangeEvents);

        if (links.Count == 0)
            return EmptyChain(fromTime, toTime);

        // ── Step 4: Compute overall title and summary ─────────────────────────
        var overallConfidence = links.Count > 0
            ? (int)links.Average(l => l.ConfidencePercent)
            : 50;

        var title   = BuildTitle(links);
        var summary = BuildSummary(links, fromTime, toTime);

        var resolvedAt = rangeEvents
            .Where(e => e.Type == TimelineEventType.InsightResolved)
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefault()?.OccurredAt;

        bool isResolved = resolvedAt.HasValue &&
                          !rangeEvents.Any(e =>
                              e.Type == TimelineEventType.InsightOnset &&
                              e.OccurredAt > resolvedAt.Value);

        return new CausalChain(
            Title:                    title,
            Summary:                  summary,
            Links:                    links,
            StartedAt:                fromTime,
            ResolvedAt:               resolvedAt,
            IsResolved:               isResolved,
            OverallConfidencePercent: overallConfidence);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static CausalChainLink? DetectTelemetryShift(
        IReadOnlyList<HistoricalTelemetryPoint> series,
        DateTimeOffset                          windowStart,
        DateTimeOffset                          eventTime)
    {
        var lookbackStart = eventTime - TimeSpan.FromMinutes(5);
        var windowEnd     = eventTime;

        var before = series
            .Where(p => p.Timestamp >= windowStart && p.Timestamp < lookbackStart + TimeSpan.FromMinutes(1))
            .ToList();
        var after  = series
            .Where(p => p.Timestamp >= lookbackStart && p.Timestamp <= windowEnd)
            .ToList();

        if (before.Count < 3 || after.Count < 3)
            return null;

        double cpuBefore  = before.Average(p => p.CpuPercent);
        double ramBefore  = before.Average(p => p.RamPercent);
        double cpuAfter   = after.Average(p => p.CpuPercent);
        double ramAfter   = after.Average(p => p.RamPercent);

        double cpuDelta = cpuAfter - cpuBefore;
        double ramDelta = ramAfter - ramBefore;

        if (Math.Abs(cpuDelta) < 8 && Math.Abs(ramDelta) < 5)
            return null;

        var parts = new List<string>();
        if (cpuDelta > 8)
            parts.Add($"CPU rose by approximately {cpuDelta:F0}%");
        if (ramDelta > 5)
            parts.Add($"RAM usage increased by approximately {ramDelta:F0}%");
        if (cpuDelta < -8)
            parts.Add($"CPU dropped by approximately {Math.Abs(cpuDelta):F0}%");

        return new CausalChainLink(
            EventTitle:           "Telemetry shift detected",
            Description:          string.Join("; ", parts) + " before conditions changed.",
            OccurredAt:           lookbackStart,
            Kind:                 CausalLinkKind.TelemetryShift,
            ConfidencePercent:    75,
            AttributedProcesses:  []);
    }

    private static CausalChainLink? BuildLinkForEvent(
        TimelineEvent  ev,
        TimelineEvent? previous)
    {
        int confidence = TemporalConfidence(ev, previous);

        return ev.Type switch
        {
            TimelineEventType.InsightOnset => new CausalChainLink(
                EventTitle:           ev.Title,
                Description:          ev.Detail ?? $"Condition detected: {ev.Title}",
                OccurredAt:           ev.OccurredAt,
                Kind:                 CausalLinkKind.InsightOnset,
                ConfidencePercent:    confidence,
                AttributedProcesses:  ev.AttributedProcesses ?? []),

            TimelineEventType.ForecastAlert => new CausalChainLink(
                EventTitle:           ev.Title,
                Description:          ev.Detail ?? "A forecast rule escalated its warning level.",
                OccurredAt:           ev.OccurredAt,
                Kind:                 CausalLinkKind.ForecastEscalation,
                ConfidencePercent:    Math.Max(confidence - 10, 50),
                AttributedProcesses:  []),

            TimelineEventType.SynthesisCorrelated => new CausalChainLink(
                EventTitle:           ev.Title,
                Description:          ev.Detail ?? "Multiple conditions were found to share a likely common cause.",
                OccurredAt:           ev.OccurredAt,
                Kind:                 CausalLinkKind.SynthesisCorrelation,
                ConfidencePercent:    Math.Max(confidence - 5, 55),
                AttributedProcesses:  ev.AttributedProcesses ?? []),

            TimelineEventType.ActionExecuted => new CausalChainLink(
                EventTitle:           $"Action taken: {ev.Title}",
                Description:          ev.Detail ?? "A remediation action was executed.",
                OccurredAt:           ev.OccurredAt,
                Kind:                 CausalLinkKind.ActionTaken,
                ConfidencePercent:    100,          // actions are factual
                AttributedProcesses:  []),

            TimelineEventType.InsightResolved => new CausalChainLink(
                EventTitle:           $"Condition resolved: {ev.Title}",
                Description:          "The condition that triggered this insight is no longer detected.",
                OccurredAt:           ev.OccurredAt,
                Kind:                 CausalLinkKind.Stabilization,
                ConfidencePercent:    confidence,
                AttributedProcesses:  []),

            TimelineEventType.ProcessAnomaly => new CausalChainLink(
                EventTitle:           ev.Title,
                Description:          ev.Detail ?? "A process behavioral anomaly was detected.",
                OccurredAt:           ev.OccurredAt,
                Kind:                 CausalLinkKind.ProcessEscalation,
                ConfidencePercent:    confidence,
                AttributedProcesses:  ev.AttributedProcesses ?? []),

            _ => null,
        };
    }

    private static void AugmentWithStabilizationLinks(
        List<CausalChainLink> links,
        List<TimelineEvent>   events)
    {
        // Find action → resolution pairs and boost their confidence
        for (int i = 0; i < links.Count; i++)
        {
            if (links[i].Kind != CausalLinkKind.ActionTaken) continue;
            var actionTime = links[i].OccurredAt;

            bool hasSubsequentResolution = links.Any(l =>
                l.Kind == CausalLinkKind.Stabilization &&
                l.OccurredAt > actionTime &&
                l.OccurredAt - actionTime <= ActionEffectWindow);

            if (hasSubsequentResolution)
            {
                // Upgrade the action link's description
                links[i] = links[i] with
                {
                    Description      = links[i].Description +
                                       " Conditions improved following this action.",
                    ConfidencePercent = Math.Min(links[i].ConfidencePercent + 10, 95),
                };
            }
        }
    }

    private static int TemporalConfidence(TimelineEvent ev, TimelineEvent? previous)
    {
        if (previous is null) return 70;

        var gap = ev.OccurredAt - previous.OccurredAt;
        if (gap <= StrongCorrelationWindow)   return 82;
        if (gap <= ModerateCorrelationWindow) return 72;
        if (gap <= WeakCorrelationWindow)     return 62;
        return 55;
    }

    private static string BuildTitle(IReadOnlyList<CausalChainLink> links)
    {
        var onsets = links.Where(l => l.Kind == CausalLinkKind.InsightOnset).ToList();
        if (onsets.Count == 0) return "System activity sequence";
        if (onsets.Count == 1) return $"Condition sequence: {onsets[0].EventTitle}";
        return $"Multi-factor condition sequence ({onsets.Count} findings)";
    }

    private static string BuildSummary(
        IReadOnlyList<CausalChainLink> links,
        DateTimeOffset                 fromTime,
        DateTimeOffset                 toTime)
    {
        var duration = toTime - fromTime;
        var durationText = duration.TotalMinutes < 2
            ? "under a minute"
            : duration.TotalMinutes < 60
                ? $"{(int)duration.TotalMinutes} minutes"
                : $"{(int)duration.TotalHours}h {duration.Minutes}m";

        int onsetCount    = links.Count(l => l.Kind == CausalLinkKind.InsightOnset);
        int actionCount   = links.Count(l => l.Kind == CausalLinkKind.ActionTaken);
        bool hasResolved  = links.Any(l => l.Kind == CausalLinkKind.Stabilization);

        var parts = new List<string>();
        if (onsetCount > 0)
            parts.Add($"{onsetCount} condition(s) detected over {durationText}");
        if (actionCount > 0)
            parts.Add($"{actionCount} remediation(s) taken");
        if (hasResolved)
            parts.Add("conditions subsequently resolved");

        return parts.Count > 0
            ? string.Join("; ", parts) + "."
            : $"System activity observed over {durationText}.";
    }

    private static CausalChain EmptyChain(DateTimeOffset from, DateTimeOffset to) =>
        new(
            Title:                    "No significant events",
            Summary:                  "No significant conditions or events were detected in this window.",
            Links:                    [],
            StartedAt:                from,
            ResolvedAt:               null,
            IsResolved:               false,
            OverallConfidencePercent: 0);
}
