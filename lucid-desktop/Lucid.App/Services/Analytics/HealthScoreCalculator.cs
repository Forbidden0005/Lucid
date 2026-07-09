using Lucid.Services.Persistence;

namespace Lucid.Services.Analytics;

/// <summary>
/// Pure, deterministic health-score math, split out of
/// <see cref="HistoricalAnalyticsEngine"/> so it can be unit-tested without
/// touching SQLite and reused by the score-breakdown UI.
///
/// Design intent — score by DISTINCT CONDITION, not raw occurrence count:
///   The insight history stores one row per onset. A single ongoing condition
///   (e.g. "disk nearly full") re-fires many times across a week. Penalising
///   every raw occurrence let the score collapse to 0 from one persistent
///   problem — which is exactly the "0/100 Needs Attention" the dashboard
///   showed. Grouping by InsightId makes the score reflect how many things are
///   actually wrong, with a bounded recurrence bump so a nagging issue still
///   costs a little more than a one-off.
///
/// Severity ordinals (from the insight engine):
///   ≥ 2 → Warning-class      (heavier penalty)
///     1 → Recommendation     (light penalty)
///     0 → Informational      (no penalty)
/// </summary>
public static class HealthScoreCalculator
{
    /// <summary>Points deducted per distinct warning-class condition.</summary>
    public const int PenaltyPerWarning        = 5;

    /// <summary>Points deducted per distinct recommendation-class condition.</summary>
    public const int PenaltyPerRecommendation = 2;

    /// <summary>Extra penalty when a warning-class condition recurs (≥ this many onsets).</summary>
    public const int RecurrenceOnsetThreshold = 3;

    /// <summary>Additional points deducted when a warning meets the recurrence threshold.</summary>
    public const int RecurrencePenalty        = 5;

    /// <summary>
    /// Computes the full <see cref="HealthScore"/> for a window, including the
    /// per-condition <see cref="HealthScore.Contributions"/> breakdown.
    /// </summary>
    /// <param name="current">Insight rows whose onset falls in this window.</param>
    /// <param name="previous">Insight rows for the previous equal-length window (for trend).</param>
    /// <param name="window">The window length (for display metadata).</param>
    public static HealthScore Compute(
        IReadOnlyList<PersistedInsightRecord> current,
        IReadOnlyList<PersistedInsightRecord> previous,
        TimeSpan window)
    {
        // Defensive: repositories should never hand us null, but on-disk data
        // is data — treat null as empty rather than throwing into the UI.
        current  ??= [];
        previous ??= [];

        var contributions = BuildContributions(current);
        int score         = ClampScore(100 - contributions.Sum(c => c.PointsDeducted));

        // Raw occurrence counts are kept for backward-compatible display fields.
        int warnings = current.Count(r => r.SeverityOrdinal >= 2);
        int recs     = current.Count(r => r.SeverityOrdinal == 1);

        string label = LabelFor(score);

        int prevScore = ClampScore(100 - BuildContributions(previous).Sum(c => c.PointsDeducted));
        var trend = (score - prevScore) switch
        {
            > 5  => TrendDirection.Improving,
            < -5 => TrendDirection.Worsening,
            _    => TrendDirection.Stable,
        };

        return new HealthScore(score, window, warnings, recs, label, trend)
        {
            Contributions = contributions,
        };
    }

    /// <summary>Groups raw onset rows into one bounded contribution per condition.</summary>
    public static IReadOnlyList<HealthScoreContribution> BuildContributions(
        IReadOnlyList<PersistedInsightRecord> records)
    {
        if (records is null || records.Count == 0)
            return [];

        var list = new List<HealthScoreContribution>();

        foreach (var group in records.GroupBy(r => r.InsightId))
        {
            var occurrences = group.Count();
            var maxSeverity = group.Max(r => r.SeverityOrdinal);
            if (maxSeverity < 1)
                continue; // informational-only condition — no score impact

            // Latest row supplies the display title (titles can evolve over time)
            // and tells us whether the condition is still active.
            var latest = group.OrderByDescending(r => r.OnsetAt).First();
            bool isActive = latest.ResolvedAt is null;

            int points;
            if (maxSeverity >= 2)
            {
                points = PenaltyPerWarning;
                if (occurrences >= RecurrenceOnsetThreshold)
                    points += RecurrencePenalty;
            }
            else
            {
                points = PenaltyPerRecommendation;
            }

            list.Add(new HealthScoreContribution(
                InsightId:       group.Key,
                Title:           latest.Title,
                SeverityOrdinal: maxSeverity,
                Occurrences:     occurrences,
                PointsDeducted:  points,
                FirstSeen:       group.Min(r => r.OnsetAt),
                LastSeen:        latest.OnsetAt,
                IsActive:        isActive));
        }

        return list
            .OrderByDescending(c => c.PointsDeducted)
            .ThenByDescending(c => c.Occurrences)
            .ToList();
    }

    /// <summary>Maps a clamped score value to its human-readable label.</summary>
    public static string LabelFor(int score) => score switch
    {
        >= 90 => "Excellent",
        >= 75 => "Good",
        >= 55 => "Fair",
        >= 35 => "Poor",
        _     => "Needs Attention",
    };

    private static int ClampScore(int raw) => Math.Clamp(raw, 0, 100);
}
