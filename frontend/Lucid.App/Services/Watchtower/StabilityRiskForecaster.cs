using Lucid.Services.Analytics;

namespace Lucid.Services.Watchtower;

/// <summary>
/// Computes forward-looking <see cref="StabilityRiskForecast"/> records and a
/// <see cref="StabilityOutlook"/> from the current alerts and historical trends.
///
/// All projections are linear extrapolations from observed averages.
/// No probabilistic models, no ML — purely deterministic.
///
/// Risk scores are composite signal strengths, not probabilities.
/// Language is always confidence-aware: "may", "suggests", "appears".
/// </summary>
public static class StabilityRiskForecaster
{
    // ── Forecast thresholds ───────────────────────────────────────────────────
    private const double DiskNearFullThreshold  = 85.0;   // >85% disk usage = near full
    private const double DiskWarningThreshold   = 75.0;   // >75% trending → forecast
    private const double MetricHighThreshold    = 70.0;   // avg > 70% = high utilisation
    private const int    DaysToImpactFromSlope  = 30;     // default projection window

    // ── Outlook thresholds ────────────────────────────────────────────────────
    private const int OutlookExcellent = 88;
    private const int OutlookGood      = 74;
    private const int OutlookFair      = 59;

    /// <summary>
    /// Builds stability risk forecasts from metric trends and active alerts.
    /// Returns at most 4 forecasts, ordered by risk score descending.
    /// </summary>
    public static IReadOnlyList<StabilityRiskForecast> BuildForecasts(
        HistoricalSummary              summary,
        IReadOnlyList<WatchtowerAlert> alerts)
    {
        var forecasts = new List<StabilityRiskForecast>();
        var now       = DateTimeOffset.Now;

        // ── 1. Disk exhaustion projection ────────────────────────────────────
        if (summary.DiskTrend.Direction == TrendDirection.Worsening
            && summary.DiskTrend.SevenDayAvg.HasValue)
        {
            var avg7 = summary.DiskTrend.SevenDayAvg.Value;
            var avg30 = summary.DiskTrend.ThirtyDayAvg;

            if (avg7 >= DiskWarningThreshold)
            {
                int? daysToImpact = null;

                if (avg30.HasValue && avg30.Value > 0 && avg7 > avg30.Value)
                {
                    // Linear rate: (avg7 - avg30) per ~3 weeks → project to threshold
                    var weeklyRise = (avg7 - avg30.Value) / 3.0;
                    if (weeklyRise > 0)
                    {
                        var pointsToThreshold = DiskNearFullThreshold - avg7;
                        var weeksToImpact     = pointsToThreshold / weeklyRise;
                        daysToImpact          = Math.Clamp((int)(weeksToImpact * 7), 3, 180);
                    }
                }

                var riskScore = avg7 >= 80 ? 78 : 55;
                forecasts.Add(new StabilityRiskForecast(
                    ForecastId:          "forecast.disk.pressure",
                    Title:               "Disk usage may approach capacity",
                    Description:         $"Disk utilisation is currently around {avg7:F0}% and trending " +
                                         "upward. If this trend continues, available disk space may " +
                                         "become constrained.",
                    DaysToImpact:        daysToImpact,
                    RiskScore:           riskScore,
                    ConfidencePercent:   avg30.HasValue ? 68 : 45,
                    ContributingSignals: BuildDiskSignals(summary, alerts),
                    GeneratedAt:         now));
            }
        }

        // ── 2. RAM pressure projection ───────────────────────────────────────
        if (summary.RamTrend.Direction == TrendDirection.Worsening
            && summary.RamTrend.SevenDayAvg.HasValue
            && summary.RamTrend.SevenDayAvg.Value >= MetricHighThreshold)
        {
            forecasts.Add(new StabilityRiskForecast(
                ForecastId:          "forecast.ram.pressure",
                Title:               "RAM usage is trending higher than usual",
                Description:         $"Memory utilisation has averaged {summary.RamTrend.SevenDayAvg:F0}% " +
                                     "over the past 7 days, which is above the 30-day baseline. " +
                                     "Systems with sustained high RAM usage may experience slower " +
                                     "application responsiveness over time.",
                DaysToImpact:        null,
                RiskScore:           50,
                ConfidencePercent:   65,
                ContributingSignals: [
                    $"7-day average: {summary.RamTrend.SevenDayAvg:F0}%",
                    $"30-day average: {summary.RamTrend.ThirtyDayAvg:F0}%",
                    summary.RamTrend.TrendLabel,
                ],
                GeneratedAt:         now));
        }

        // ── 3. Recurring pattern escalation risk ──────────────────────────────
        var escalating = summary.RecurringPatterns.Where(p => p.IsEscalating).ToList();
        if (escalating.Count >= 2)
        {
            var titles = string.Join(" and ", escalating.Take(2).Select(p => $"\"{p.Title}\""));
            forecasts.Add(new StabilityRiskForecast(
                ForecastId:          "forecast.patterns.escalation",
                Title:               "Recurring issues may become more disruptive",
                Description:         $"Patterns including {titles} are occurring with increasing " +
                                     "frequency. If root causes are not addressed, these may become " +
                                     "more frequent over the coming weeks.",
                DaysToImpact:        DaysToImpactFromSlope,
                RiskScore:           45,
                ConfidencePercent:   60,
                ContributingSignals: escalating.Take(3).Select(p =>
                    $"{p.Title}: {p.TotalOccurrences} occurrences ({p.FrequencyLabel})").ToList(),
                GeneratedAt:         now));
        }

        // ── 4. Health decline projection ──────────────────────────────────────
        var decline = summary.ThirtyDayHealth.Score - summary.SevenDayHealth.Score;
        if (decline >= 12 && summary.SevenDayHealth.Score < 75)
        {
            forecasts.Add(new StabilityRiskForecast(
                ForecastId:          "forecast.health.decline",
                Title:               "System health may continue to decline",
                Description:         $"The 7-day health score ({summary.SevenDayHealth.Score}) has " +
                                     $"dropped {decline} points below the 30-day average " +
                                     $"({summary.ThirtyDayHealth.Score}). Without addressing the " +
                                     "contributing patterns, further decline is possible.",
                DaysToImpact:        null,
                RiskScore:           40,
                ConfidencePercent:   58,
                ContributingSignals: [
                    $"7-day health: {summary.SevenDayHealth.Score} ({summary.SevenDayHealth.Label})",
                    $"30-day health: {summary.ThirtyDayHealth.Score} ({summary.ThirtyDayHealth.Label})",
                    $"Score declined {decline} points",
                ],
                GeneratedAt:         now));
        }

        forecasts.Sort((a, b) => b.RiskScore.CompareTo(a.RiskScore));
        return forecasts.Take(4).ToList();
    }

    /// <summary>
    /// Computes the headline <see cref="StabilityOutlook"/> from the current snapshot inputs.
    /// </summary>
    public static StabilityOutlook ComputeOutlook(
        HistoricalSummary              summary,
        IReadOnlyList<WatchtowerAlert> alerts,
        bool                           hasSufficientHistory)
    {
        if (!hasSufficientHistory)
        {
            return new StabilityOutlook(
                OverallScore:     0,
                Label:            "Building baseline",
                Color:            "#6B7280",
                NarrativeSummary: "The Watchtower is still building its baseline from operational data. " +
                                  "Check back after the system has been running for a few days.",
                LastComputedAt:   DateTimeOffset.Now);
        }

        // Composite score: start from health score, penalise for alerts
        var score = summary.SevenDayHealth.Score;

        foreach (var alert in alerts)
        {
            score -= alert.Severity switch
            {
                WatchtowerAlertSeverity.Elevated => 12,
                WatchtowerAlertSeverity.Warning  => 7,
                WatchtowerAlertSeverity.Advisory => 3,
                _                                => 1,
            };
        }

        score = Math.Clamp(score, 0, 100);

        var (label, color) = score switch
        {
            >= OutlookExcellent => ("Stable",             "#34D399"),
            >= OutlookGood      => ("Monitoring",          "#60A5FA"),
            >= OutlookFair      => ("Attention Suggested", "#F59E0B"),
            _                   => ("Review Recommended",  "#EF4444"),
        };

        var narrative = BuildNarrative(score, summary, alerts);

        return new StabilityOutlook(
            OverallScore:     score,
            Label:            label,
            Color:            color,
            NarrativeSummary: narrative,
            LastComputedAt:   DateTimeOffset.Now);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> BuildDiskSignals(
        HistoricalSummary              summary,
        IReadOnlyList<WatchtowerAlert> alerts)
    {
        var signals = new List<string>();

        if (summary.DiskTrend.SevenDayAvg.HasValue)
            signals.Add($"7-day disk avg: {summary.DiskTrend.SevenDayAvg:F0}%");

        if (summary.DiskTrend.ThirtyDayAvg.HasValue)
            signals.Add($"30-day disk avg: {summary.DiskTrend.ThirtyDayAvg:F0}%");

        if (!string.IsNullOrEmpty(summary.DiskTrend.TrendLabel))
            signals.Add(summary.DiskTrend.TrendLabel);

        var diskAlert = alerts.FirstOrDefault(a => a.AffectedMetric == "Disk");
        if (diskAlert is not null)
            signals.Add($"Active alert: {diskAlert.Title}");

        return signals;
    }

    private static string BuildNarrative(
        int                            score,
        HistoricalSummary              summary,
        IReadOnlyList<WatchtowerAlert> alerts)
    {
        var alertSummary = alerts.Count switch
        {
            0 => "No active watchtower alerts.",
            1 => $"One signal detected: {alerts[0].Title}.",
            2 => $"Two signals detected, including {alerts[0].Title}.",
            _ => $"{alerts.Count} signals detected, including {alerts[0].Title}.",
        };

        var healthText = summary.SevenDayHealth.Score >= 80
            ? "System health has been good this week."
            : summary.SevenDayHealth.Score >= 65
                ? "System health has been somewhat mixed this week."
                : "System health this week appears lower than the 30-day baseline.";

        return $"{healthText} {alertSummary} Overall stability outlook: {ScoreToNarrative(score)}.";
    }

    private static string ScoreToNarrative(int score) => score switch
    {
        >= OutlookExcellent => "the system appears to be operating normally with no significant concerns",
        >= OutlookGood      => "the system is generally stable with a few patterns worth monitoring",
        >= OutlookFair      => "there are a few patterns that may benefit from attention",
        _                   => "there are several patterns that suggest a closer review may be helpful",
    };
}
