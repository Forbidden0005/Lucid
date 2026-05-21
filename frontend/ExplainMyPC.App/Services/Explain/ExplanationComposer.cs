using ExplainMyPC.Services.Baseline;
using ExplainMyPC.Services.Intelligence;
using ExplainMyPC.Services.Timeline;

namespace ExplainMyPC.Services.Explain;

/// <summary>
/// Composes the non-causal sections of an <see cref="OperationalExplanation"/>:
///   • "What Changed?"      — derived from recent timeline events
///   • "What Happens Next?" — derived from forecast-class insight rules
///   • "Why We Believe This" — derived from baseline data and attribution evidence
///
/// All output is deterministic and confidence-aware. No LLM or cloud service is used.
/// </summary>
internal static class ExplanationComposer
{
    private static readonly TimeSpan ChangeWindow = TimeSpan.FromHours(4);

    // ── What Changed? ─────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts meaningful recent changes from the timeline.
    /// Only includes events that represent observable state transitions
    /// (insight onset/clear, forecast alerts, action executions, correlations).
    /// Excludes low-signal session events (idle begin/end).
    /// Returns up to 8 items, most-recent first.
    /// </summary>
    internal static IReadOnlyList<RecentChangeItem> ComposeRecentChanges(
        IReadOnlyList<TimelineEvent> events)
    {
        var cutoff = DateTimeOffset.Now - ChangeWindow;
        var items  = new List<RecentChangeItem>();

        foreach (var ev in events) // events are newest-first
        {
            if (ev.OccurredAt < cutoff) break;

            var include = ev.Type is
                TimelineEventType.InsightOnset       or
                TimelineEventType.InsightResolved    or
                TimelineEventType.ForecastAlert      or
                TimelineEventType.ActionExecuted     or
                TimelineEventType.ActionRollback     or
                TimelineEventType.ActionFailed       or
                TimelineEventType.SynthesisCorrelated or
                TimelineEventType.NarrativeCheckpoint;

            if (!include) continue;

            items.Add(new RecentChangeItem(
                Title:      ev.Title,
                Detail:     ev.Detail ?? string.Empty,
                OccurredAt: ev.OccurredAt,
                Severity:   ev.Severity,
                SourceType: ev.Type));

            if (items.Count >= 8) break;
        }

        return items;
    }

    // ── What Happens Next? ────────────────────────────────────────────────────

    /// <summary>
    /// Extracts forecast projections from forecast-prefixed insight rules.
    /// Returns one ForecastOutlook per active forecast insight, ordered by severity.
    /// </summary>
    internal static IReadOnlyList<ForecastOutlook> ComposeForecasts(
        IReadOnlyList<SystemInsight> insights)
    {
        var forecasts = new List<ForecastOutlook>();

        foreach (var insight in insights
            .Where(i => i.Id.StartsWith("forecast.", StringComparison.Ordinal))
            .OrderByDescending(i => (int)i.Severity)
            .ThenByDescending(i => i.ConfidencePercent))
        {
            var severity = insight.Severity switch
            {
                InsightSeverity.Warning        => ForecastSeverity.Warning,
                InsightSeverity.Recommendation => ForecastSeverity.Watch,
                _                              => ForecastSeverity.Stable,
            };

            forecasts.Add(new ForecastOutlook(
                Title:             insight.Title,
                Projection:        insight.Detail,
                Severity:          severity,
                MitigationHint:    insight.ActionHint,
                ConfidencePercent: insight.ConfidencePercent));
        }

        return forecasts;
    }

    // ── Why We Believe This ───────────────────────────────────────────────────

    /// <summary>
    /// Builds the evidence set that substantiates the operational explanation.
    /// Includes: data breadth, baseline readiness, process attributions, and
    /// an explicit uncertainty acknowledgment.
    /// </summary>
    internal static IReadOnlyList<EvidenceItem> ComposeEvidence(
        IReadOnlyList<SystemInsight> insights,
        MachineBaseline?             baseline,
        int                          historyCount)
    {
        var evidence = new List<EvidenceItem>();

        // ── Data breadth ──────────────────────────────────────────────────────
        evidence.Add(new EvidenceItem(
            Label: "Telemetry readings",
            Value: historyCount > 0
                ? $"{historyCount:N0} samples in the rolling history window"
                : "Collecting — check back in a few seconds",
            Kind: EvidenceKind.DirectMeasurement));

        var actionable = insights.Where(i => i.Id != "system.healthy").ToList();
        evidence.Add(new EvidenceItem(
            Label: "Rules evaluated",
            Value: actionable.Count == 0
                ? "All conditions checked — none flagged"
                : $"{insights.Count} rules checked, {actionable.Count} condition(s) active",
            Kind: EvidenceKind.DirectMeasurement));

        // ── Baseline comparisons ──────────────────────────────────────────────
        if (baseline is not null)
        {
            if (baseline.IdleCpuReady)
                evidence.Add(new EvidenceItem(
                    Label: "CPU baseline",
                    Value: $"Idle mean {baseline.IdleCpuMean:F1}% ± {baseline.IdleCpuStdDev:F1}% " +
                           $"(from {baseline.IdleCpuSamples:N0} samples)",
                    Kind: EvidenceKind.BaselineComparison));

            if (baseline.NormalRamReady)
                evidence.Add(new EvidenceItem(
                    Label: "RAM baseline",
                    Value: $"Normal mean {baseline.NormalRamMean:F1}% ± {baseline.NormalRamStdDev:F1}% " +
                           $"(from {baseline.NormalRamSamples:N0} samples)",
                    Kind: EvidenceKind.BaselineComparison));

            if (baseline.NormalThermalReady)
                evidence.Add(new EvidenceItem(
                    Label: "Thermal baseline",
                    Value: $"Normal mean {baseline.NormalThermalMean:F1}°C ± {baseline.NormalThermalStdDev:F1}°C " +
                           $"(from {baseline.NormalThermalSamples:N0} samples)",
                    Kind: EvidenceKind.BaselineComparison));

            evidence.Add(new EvidenceItem(
                Label: "Learning history",
                Value: $"Active since {baseline.LearnedSince:MMM d, yyyy} — " +
                       $"{baseline.TotalObservations:N0} total observations",
                Kind: EvidenceKind.BaselineComparison));
        }
        else
        {
            evidence.Add(new EvidenceItem(
                Label: "Baseline",
                Value: "Still learning your machine's normal ranges. " +
                       "Machine-specific comparisons will be available after ~5 minutes.",
                Kind: EvidenceKind.BaselineComparison));
        }

        // ── Process attributions ──────────────────────────────────────────────
        var attributions = insights
            .SelectMany(i => i.AttributedProcesses)
            .GroupBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(a => a.UsageValue).First())
            .OrderByDescending(a => a.UsageValue)
            .Take(3)
            .ToList();

        foreach (var attr in attributions)
        {
            evidence.Add(new EvidenceItem(
                Label: $"{attr.KindLabel} attribution",
                Value: $"{attr.DisplayName} — {attr.UsageText} ({attr.ConfidencePercent}% confidence)",
                Kind:  EvidenceKind.ProcessAttribution));
        }

        // ── Uncertainty acknowledgment ────────────────────────────────────────
        evidence.Add(new EvidenceItem(
            Label: "About this analysis",
            Value: "All findings are produced deterministically from local sensor readings. " +
                   "Confidence scores reflect data availability and measurement precision. " +
                   "No cloud services, no LLM, no external data sources.",
            Kind: EvidenceKind.DirectMeasurement));

        return evidence;
    }
}
