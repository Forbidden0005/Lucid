using ExplainMyPC.Services.History;
using ExplainMyPC.Services.Timeline;

namespace ExplainMyPC.Services.Replay;

/// <summary>
/// Composes plain-English historical narratives from replay session data.
///
/// Narratives explain what happened over time in a session — how the system
/// behaved, what conditions developed, what actions were taken, and what effects
/// those actions had.
///
/// Language rules (from CLAUDE.md security doctrine):
///   NEVER use: "malicious", "infected", "dangerous", "threat detected"
///   ALWAYS use: "unusual", "unexpected", "worth reviewing", confidence-aware framing
///   All findings are probabilistic, not absolute.
/// </summary>
internal static class ReplayNarrativeComposer
{
    /// <summary>
    /// Composes a narrative summarising the session from start to <paramref name="upToTime"/>.
    /// </summary>
    public static ReplayNarrative ComposeSessionNarrative(
        ReplaySession  session,
        DateTimeOffset upToTime)
    {
        if (!session.HasTelemetry)
        {
            return new ReplayNarrative(
                Title:       "No data available",
                Paragraphs:  ["No telemetry data has been collected yet. The system monitor needs a few minutes to gather enough data for a meaningful replay."],
                CoveredFrom: session.OldestDataPoint,
                CoveredTo:   upToTime,
                Kind:        ReplayNarrativeKind.SessionSummary);
        }

        var eventsToNow = session.Events
            .Where(e => e.OccurredAt <= upToTime)
            .OrderBy(e => e.OccurredAt)
            .ToList();

        var actionsToNow = session.Actions
            .Where(a => a.ExecutedAt <= upToTime && a.IsSuccess && !a.IsDryRun)
            .OrderBy(a => a.ExecutedAt)
            .ToList();

        var paragraphs = new List<string>();

        // ── Opening: coverage and data quality ───────────────────────────────
        var coverage       = upToTime - session.OldestDataPoint;
        var coverageText   = FormatDuration(coverage);
        int telemetryCount = session.TelemetrySeries.Count(p => p.Timestamp <= upToTime);

        paragraphs.Add(
            $"This replay covers approximately {coverageText} of observed system activity, " +
            $"drawing on {telemetryCount:N0} telemetry samples and {eventsToNow.Count} " +
            $"recorded events from the current session.");

        // ── Telemetry summary ────────────────────────────────────────────────
        var cpuSamples = session.TelemetrySeries
            .Where(p => p.Timestamp <= upToTime)
            .ToList();

        if (cpuSamples.Count > 0)
        {
            double cpuAvg  = cpuSamples.Average(p => p.CpuPercent);
            double cpuPeak = cpuSamples.Max(p => p.CpuPercent);
            double ramAvg  = cpuSamples.Average(p => p.RamPercent);

            paragraphs.Add(BuildTelemetrySummary(cpuAvg, cpuPeak, ramAvg, cpuSamples));
        }

        // ── Insight activity ─────────────────────────────────────────────────
        var onsets     = eventsToNow.Where(e => e.Type == TimelineEventType.InsightOnset).ToList();
        var resolutions= eventsToNow.Where(e => e.Type == TimelineEventType.InsightResolved).ToList();
        var forecasts  = eventsToNow.Where(e => e.Type == TimelineEventType.ForecastAlert).ToList();

        if (onsets.Count > 0)
        {
            paragraphs.Add(BuildInsightSummary(onsets, resolutions, forecasts));
        }
        else
        {
            paragraphs.Add("No significant system conditions were detected during this period. " +
                           "All monitored metrics remained within expected ranges.");
        }

        // ── Actions taken ────────────────────────────────────────────────────
        if (actionsToNow.Count > 0)
        {
            paragraphs.Add(BuildActionSummary(actionsToNow, eventsToNow));
        }

        // ── Closing assessment ───────────────────────────────────────────────
        paragraphs.Add(BuildClosingAssessment(onsets, resolutions, actionsToNow, cpuSamples));

        return new ReplayNarrative(
            Title:       $"Session summary — {coverageText} of activity",
            Paragraphs:  paragraphs.ToArray(),
            CoveredFrom: session.OldestDataPoint,
            CoveredTo:   upToTime,
            Kind:        ReplayNarrativeKind.SessionSummary);
    }

    /// <summary>
    /// Composes a narrative explaining the effect of a remediation action.
    /// </summary>
    public static ReplayNarrative ComposeRemediationNarrative(
        RemediationComparison comparison)
    {
        var paragraphs = new List<string>();
        var action     = comparison.Action;
        var delta      = comparison.Delta;

        // Opening
        var duration = TimeSpan.FromMilliseconds(action.DurationMs);
        paragraphs.Add(
            $"The \"{action.ActionTitle}\" action was completed in {FormatDuration(duration)}. " +
            $"The comparison below captures system state approximately 5 minutes before " +
            $"and 5 minutes after the action completed.");

        // What changed
        if (delta.HasSignificantChange && delta.SummaryLines.Count > 0)
        {
            var changesText = string.Join(" ", delta.SummaryLines.Take(3));
            paragraphs.Add($"Following the action, notable changes were observed: {changesText}");
        }
        else if (!delta.HasSignificantChange)
        {
            paragraphs.Add(
                "No statistically significant changes in hardware metrics were detected " +
                "in the comparison window. This may indicate the action's effect is gradual, " +
                "affects future startup behavior, or that the comparison window was too narrow " +
                "to capture the full effect.");
        }

        // Interpretation
        paragraphs.Add(comparison.ShowsImprovement
            ? "Overall, the system appears to have benefited from this action. " +
              "Hardware metrics moved in a favorable direction in the period following execution."
            : "The immediate hardware impact of this action was not clearly measurable in the " +
              "comparison window. Some actions — like cleaning temporary files or managing startup " +
              "programs — have effects that appear over longer time periods or at next startup.");

        return new ReplayNarrative(
            Title:       $"Before/after: {action.ActionTitle}",
            Paragraphs:  paragraphs.ToArray(),
            CoveredFrom: comparison.SnapshotBefore.Timestamp,
            CoveredTo:   comparison.SnapshotAfter.Timestamp,
            Kind:        ReplayNarrativeKind.RemediationEffect);
    }

    // ── Private builders ──────────────────────────────────────────────────────

    private static string BuildTelemetrySummary(
        double                             cpuAvg,
        double                             cpuPeak,
        double                             ramAvg,
        List<HistoricalTelemetryPoint>     samples)
    {
        var cpuLevel  = cpuAvg > 70 ? "elevated" : cpuAvg > 40 ? "moderate" : "low";
        var ramLevel  = ramAvg > 80 ? "high"     : ramAvg > 55 ? "moderate" : "normal";

        var sb = new System.Text.StringBuilder();
        sb.Append($"CPU activity averaged {cpuAvg:F0}% (peak {cpuPeak:F0}%), " +
                  $"which is {cpuLevel} for a typical session. ");
        sb.Append($"RAM usage averaged {ramAvg:F0}%, which is {ramLevel}. ");

        bool hasTemp = samples.Any(p => p.HasTemperature);
        if (hasTemp)
        {
            double tempAvg  = samples.Where(p => p.HasTemperature).Average(p => p.TemperatureCelsius);
            double tempPeak = samples.Where(p => p.HasTemperature).Max(p => p.TemperatureCelsius);
            sb.Append($"CPU temperature averaged {tempAvg:F0}°C, peaking at {tempPeak:F0}°C.");
        }

        return sb.ToString();
    }

    private static string BuildInsightSummary(
        List<TimelineEvent> onsets,
        List<TimelineEvent> resolutions,
        List<TimelineEvent> forecasts)
    {
        int resolvedCount = resolutions.Count;
        int activeCount   = onsets.Count - resolvedCount;
        activeCount       = Math.Max(activeCount, 0);

        var parts = new List<string>();
        parts.Add($"During this period, {onsets.Count} system condition(s) were detected.");

        if (resolvedCount > 0)
            parts.Add($"{resolvedCount} resolved on their own or following user action.");
        if (activeCount > 0)
            parts.Add($"{activeCount} may still be active.");
        if (forecasts.Count > 0)
            parts.Add($"{forecasts.Count} forecast alert(s) indicated potential future issues.");

        // List notable findings
        var notable = onsets
            .Where(e => e.Severity >= TimelineEventSeverity.Warning)
            .Take(3)
            .Select(e => e.Title)
            .ToList();

        if (notable.Count > 0)
            parts.Add($"Notable findings: {string.Join("; ", notable)}.");

        return string.Join(" ", parts);
    }

    private static string BuildActionSummary(
        List<OperationRecord> actions,
        List<TimelineEvent>   events)
    {
        var actionNames = actions.Select(a => $"\"{a.ActionTitle}\"").ToList();
        var actionText  = actions.Count == 1
            ? $"One remediation action was completed: {actionNames[0]}."
            : $"{actions.Count} remediation actions were completed: {string.Join(", ", actionNames)}.";

        // Check if any insights resolved after the first action
        if (actions.Count > 0)
        {
            var firstActionTime = actions.Min(a => a.ExecutedAt);
            int resolvedAfter   = events.Count(e =>
                e.Type == TimelineEventType.InsightResolved &&
                e.OccurredAt > firstActionTime);

            if (resolvedAfter > 0)
                actionText += $" {resolvedAfter} condition(s) resolved in the period following the action(s).";
        }

        return actionText;
    }

    private static string BuildClosingAssessment(
        List<TimelineEvent>   onsets,
        List<TimelineEvent>   resolutions,
        List<OperationRecord> actions,
        List<HistoricalTelemetryPoint> samples)
    {
        if (onsets.Count == 0)
            return "Overall, this was a stable period with no notable system conditions detected.";

        bool allResolved = resolutions.Count >= onsets.Count;
        bool hadActions  = actions.Count > 0;

        if (allResolved && hadActions)
            return "All detected conditions resolved during this period. The remediation actions " +
                   "taken may have contributed to this improvement.";

        if (allResolved)
            return "All detected conditions resolved during this period, suggesting the conditions " +
                   "were temporary or self-correcting.";

        if (hadActions)
            return "Remediation actions were taken during this period. Conditions that remain active " +
                   "may require additional investigation or actions targeting their root cause.";

        return "Some conditions detected during this period may still be active. " +
               "The Explain My PC page shows the current state and recommended next steps.";
    }

    private static string FormatDuration(TimeSpan t)
    {
        if (t.TotalSeconds < 90)  return $"{(int)t.TotalSeconds} seconds";
        if (t.TotalMinutes < 90)  return $"{(int)t.TotalMinutes} minutes";
        if (t.TotalHours   < 24)  return $"{t.Hours}h {t.Minutes}m";
        return $"{(int)t.TotalDays} day(s)";
    }
}
