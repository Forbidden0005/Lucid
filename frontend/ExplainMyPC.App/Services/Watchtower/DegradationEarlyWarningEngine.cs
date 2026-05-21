using ExplainMyPC.Services.Analytics;

namespace ExplainMyPC.Services.Watchtower;

/// <summary>
/// Analyzes a <see cref="HistoricalSummary"/> for early signs of system degradation.
///
/// Detection categories:
///   • Low health scores (below advisory/warning thresholds)
///   • Health score decline (7-day worse than 30-day)
///   • Escalating recurring insight patterns
///   • Metric trends worsening beyond threshold
///
/// Philosophy:
///   NEVER uses alarm language ("critical", "dangerous", "infected").
///   ALWAYS uses observation language ("appears to be", "may suggest", "worth reviewing").
///
/// All analysis is deterministic — same inputs produce same outputs.
/// </summary>
public static class DegradationEarlyWarningEngine
{
    // ── Detection thresholds ──────────────────────────────────────────────────

    // Health score thresholds
    private const int ScoreElevatedThreshold = 60;    // Below this → Elevated
    private const int ScoreWarningThreshold  = 72;    // Below this → Warning
    private const int ScoreAdvisoryThreshold = 82;    // Below this → Advisory

    // Health score decline threshold (7-day vs 30-day gap)
    private const int ScoreDeclineAdvisory = 8;       // 8-point drop → Advisory
    private const int ScoreDeclineWarning  = 15;      // 15-point drop → Warning

    // Recurring pattern thresholds
    private const int EscalatingPatternWarning  = 2;  // ≥2 escalating patterns → Warning
    private const int HighFrequencyOccurrences  = 7;  // ≥7 occurrences → Advisory
    private const int VeryHighFrequencyOccurrences = 12; // ≥12 → Warning

    // Metric trend worsening threshold
    private const double MetricDriftAdvisory = 10.0;  // 10% worsening → Advisory
    private const double MetricDriftWarning  = 20.0;  // 20% worsening → Warning

    /// <summary>
    /// Analyzes the given <paramref name="summary"/> and returns a list of
    /// early warning alerts sorted by severity (highest first).
    /// Returns an empty list when history is insufficient.
    /// </summary>
    public static IReadOnlyList<WatchtowerAlert> Analyze(HistoricalSummary summary)
    {
        var alerts = new List<WatchtowerAlert>();
        var now    = DateTimeOffset.Now;

        // Need at least some data to produce meaningful alerts
        if (summary.OldestDataPoint is null) return alerts;

        // ── 1. Overall health score alerts ───────────────────────────────────
        var score7  = summary.SevenDayHealth.Score;
        var score30 = summary.ThirtyDayHealth.Score;

        if (score7 < ScoreElevatedThreshold)
        {
            alerts.Add(new WatchtowerAlert(
                AlertId:          "watchtower.health.elevated",
                Title:            "System health appears lower than usual",
                Explanation:      $"The 7-day health score ({score7}) is below the typical range, " +
                                  "suggesting this machine has been experiencing more issues than usual recently. " +
                                  "Reviewing the Intelligence page may surface actionable findings.",
                SignalType:       DegradationSignalType.LowOverallHealth,
                Severity:         WatchtowerAlertSeverity.Elevated,
                DetectedAt:       now,
                ConfidencePercent: ComputeHealthConfidence(summary),
                AffectedMetric:   null,
                LinkedActionKey:  null,
                ActionHint:       "Review the Intelligence page for active findings"));
        }
        else if (score7 < ScoreWarningThreshold)
        {
            alerts.Add(new WatchtowerAlert(
                AlertId:          "watchtower.health.warning",
                Title:            "Health score is below typical range",
                Explanation:      $"The 7-day health score ({score7}) suggests the system has had more " +
                                  "active issues than usual this week. This may be worth reviewing.",
                SignalType:       DegradationSignalType.LowOverallHealth,
                Severity:         WatchtowerAlertSeverity.Warning,
                DetectedAt:       now,
                ConfidencePercent: ComputeHealthConfidence(summary),
                AffectedMetric:   null,
                LinkedActionKey:  null,
                ActionHint:       "Check the Intelligence and Repairs pages"));
        }
        else if (score7 < ScoreAdvisoryThreshold)
        {
            alerts.Add(new WatchtowerAlert(
                AlertId:          "watchtower.health.advisory",
                Title:            "Slightly elevated issue frequency this week",
                Explanation:      $"The 7-day health score ({score7}) is modestly below the typical " +
                                  "baseline, suggesting a mild increase in system issues. " +
                                  "No action needed unless this trend continues.",
                SignalType:       DegradationSignalType.LowOverallHealth,
                Severity:         WatchtowerAlertSeverity.Advisory,
                DetectedAt:       now,
                ConfidencePercent: ComputeHealthConfidence(summary),
                AffectedMetric:   null,
                LinkedActionKey:  null,
                ActionHint:       null));
        }

        // ── 2. Health score decline (7-day vs 30-day) ────────────────────────
        // Only fire this if the 30-day score was better (i.e., things have gotten worse)
        if (score30 > 0 && score7 > 0)
        {
            var decline = score30 - score7;

            if (decline >= ScoreDeclineWarning)
            {
                alerts.Add(new WatchtowerAlert(
                    AlertId:          "watchtower.health.declining.significant",
                    Title:            "Health score has declined noticeably",
                    Explanation:      $"The 7-day health score ({score7}) is {decline} points below " +
                                      $"the 30-day average ({score30}), suggesting a measurable worsening trend. " +
                                      "This may be worth investigating if the decline continues.",
                    SignalType:       DegradationSignalType.HealthScoreDecline,
                    Severity:         WatchtowerAlertSeverity.Warning,
                    DetectedAt:       now,
                    ConfidencePercent: Math.Min(90, ComputeHealthConfidence(summary) + 10),
                    AffectedMetric:   null,
                    LinkedActionKey:  null,
                    ActionHint:       "Check the Historical and Intelligence pages"));
            }
            else if (decline >= ScoreDeclineAdvisory)
            {
                alerts.Add(new WatchtowerAlert(
                    AlertId:          "watchtower.health.declining.mild",
                    Title:            "Health score is trending slightly downward",
                    Explanation:      $"The 7-day health score ({score7}) is {decline} points below " +
                                      $"the 30-day average ({score30}). This may reflect a mild increase " +
                                      "in system events recently — worth monitoring.",
                    SignalType:       DegradationSignalType.HealthScoreDecline,
                    Severity:         WatchtowerAlertSeverity.Advisory,
                    DetectedAt:       now,
                    ConfidencePercent: ComputeHealthConfidence(summary),
                    AffectedMetric:   null,
                    LinkedActionKey:  null,
                    ActionHint:       null));
            }
        }

        // ── 3. Escalating recurring patterns ─────────────────────────────────
        var escalating = summary.RecurringPatterns.Where(p => p.IsEscalating).ToList();

        if (escalating.Count >= EscalatingPatternWarning)
        {
            var patternTitles = string.Join(", ", escalating.Take(2).Select(p => $"\"{p.Title}\""));
            alerts.Add(new WatchtowerAlert(
                AlertId:          "watchtower.patterns.escalating",
                Title:            $"{escalating.Count} recurring issues appear to be increasing",
                Explanation:      $"Patterns including {patternTitles} have been occurring more " +
                                  "frequently than in previous weeks. This may indicate the underlying " +
                                  "causes are not fully resolved.",
                SignalType:       DegradationSignalType.EscalatingPattern,
                Severity:         WatchtowerAlertSeverity.Warning,
                DetectedAt:       now,
                ConfidencePercent: 75,
                AffectedMetric:   null,
                LinkedActionKey:  null,
                ActionHint:       "Review recurring patterns in the Historical page"));
        }
        else if (escalating.Count == 1)
        {
            alerts.Add(new WatchtowerAlert(
                AlertId:          $"watchtower.pattern.escalating.{escalating[0].InsightId}",
                Title:            $"\"{escalating[0].Title}\" appears to be recurring more often",
                Explanation:      $"This issue has been seen {escalating[0].TotalOccurrences} times " +
                                  $"({escalating[0].FrequencyLabel}) and appears to be increasing in frequency.",
                SignalType:       DegradationSignalType.EscalatingPattern,
                Severity:         WatchtowerAlertSeverity.Advisory,
                DetectedAt:       now,
                ConfidencePercent: 70,
                AffectedMetric:   null,
                LinkedActionKey:  null,
                ActionHint:       "Check the Historical page for pattern details"));
        }

        // ── 4. High-frequency recurring patterns (not necessarily escalating) ─
        var highFrequency = summary.RecurringPatterns
            .Where(p => !p.IsEscalating && p.TotalOccurrences >= VeryHighFrequencyOccurrences)
            .OrderByDescending(p => p.TotalOccurrences)
            .Take(1)
            .ToList();

        foreach (var pattern in highFrequency)
        {
            alerts.Add(new WatchtowerAlert(
                AlertId:          $"watchtower.pattern.frequent.{pattern.InsightId}",
                Title:            $"\"{pattern.Title}\" has occurred frequently",
                Explanation:      $"This pattern has been observed {pattern.TotalOccurrences} times " +
                                  $"({pattern.FrequencyLabel}). Addressing the root cause may reduce " +
                                  "how often it occurs.",
                SignalType:       DegradationSignalType.HighFrequencyAnomaly,
                Severity:         pattern.TotalOccurrences >= VeryHighFrequencyOccurrences
                                    ? WatchtowerAlertSeverity.Advisory
                                    : WatchtowerAlertSeverity.Info,
                DetectedAt:       now,
                ConfidencePercent: 65,
                AffectedMetric:   null,
                LinkedActionKey:  null,
                ActionHint:       "See recurring patterns in Historical"));
        }

        // ── 5. Worsening metric trends ────────────────────────────────────────
        foreach (var (trend, metricName) in new[]
        {
            (summary.CpuTrend,  "CPU"),
            (summary.RamTrend,  "RAM"),
            (summary.DiskTrend, "Disk"),
        })
        {
            if (trend.Direction != TrendDirection.Worsening) continue;
            if (trend.ChangePercent < MetricDriftAdvisory)   continue;

            var severity = trend.ChangePercent >= MetricDriftWarning
                ? WatchtowerAlertSeverity.Warning
                : WatchtowerAlertSeverity.Advisory;

            var changeText = $"{trend.ChangePercent:F0}%";

            alerts.Add(new WatchtowerAlert(
                AlertId:          $"watchtower.trend.{metricName.ToLowerInvariant()}",
                Title:            $"{metricName} utilisation appears to be trending upward",
                Explanation:      $"{metricName} usage is roughly {changeText} higher over the past 7 days " +
                                  $"compared to the 30-day average (7-day avg: {trend.SevenDayAvg:F0}%, " +
                                  $"30-day avg: {trend.ThirtyDayAvg:F0}%). " +
                                  "This may be worth monitoring if the trend continues.",
                SignalType:       DegradationSignalType.WorseningMetricTrend,
                Severity:         severity,
                DetectedAt:       now,
                ConfidencePercent: ComputeTrendConfidence(trend),
                AffectedMetric:   metricName,
                LinkedActionKey:  MetricToActionHint(metricName),
                ActionHint:       $"Review {metricName} usage in the Processes or Insights page"));
        }

        // Sort by severity descending, then by confidence descending
        alerts.Sort((a, b) =>
        {
            var sevCmp = b.Severity.CompareTo(a.Severity);
            return sevCmp != 0 ? sevCmp : b.ConfidencePercent.CompareTo(a.ConfidencePercent);
        });

        return alerts;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int ComputeHealthConfidence(HistoricalSummary summary)
    {
        // Higher confidence when we have more data
        if (summary.TotalInsightEvents > 100 && summary.TotalTelemetryRows > 5000) return 85;
        if (summary.TotalInsightEvents > 30  && summary.TotalTelemetryRows > 1000) return 70;
        return 55;
    }

    private static int ComputeTrendConfidence(LongTermMetricTrend trend)
    {
        // Confidence is lower when averages are null (sparse data)
        if (trend.SevenDayAvg is null || trend.ThirtyDayAvg is null) return 40;
        return 72;
    }

    private static string? MetricToActionHint(string metricName) =>
        metricName switch
        {
            "CPU"  => "action.cpu.open-task-manager",
            "Disk" => "action.disk.run-storage-sense",
            _      => null,
        };
}
