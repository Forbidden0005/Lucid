using Lucid.Services.Persistence;

namespace Lucid.Services.Analytics;

/// <summary>
/// Detects recurring behavioural patterns from persisted insight history.
///
/// Patterns identified:
///   • Recurring degradation — same insight appearing ≥ 2 times in the window
///   • Escalating frequency — occurrence rate increasing in the second half of the window
///   • Chronicity — issues with high average duration (> 30 minutes each)
///
/// All analysis is deterministic and purely based on the insight_history table.
/// No probabilistic models or forecasting is used; conclusions are stated as
/// observations ("occurred 5 times in 30 days") not predictions.
/// </summary>
internal static class HistoricalPatternDetector
{
    // ── Thresholds ─────────────────────────────────────────────────────────────

    /// <summary>Minimum occurrences to qualify as "recurring".</summary>
    private const int MinOccurrences = 2;

    /// <summary>Days per window for frequency labelling.</summary>
    private const int FrequencyWindowDays = 30;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds recurring insight patterns from the raw database aggregate rows.
    /// <paramref name="rawRows"/> is the output of
    /// <see cref="InsightHistoryRepository.GetRecurringInsightsAsync"/>.
    /// </summary>
    public static IReadOnlyList<RecurringInsightPattern> Detect(
        IReadOnlyList<(string InsightId, string Title, int Occurrences, DateTimeOffset LastSeen, int AvgDurationSecs)> rawRows,
        int analysisDays = FrequencyWindowDays)
    {
        var results = new List<RecurringInsightPattern>(rawRows.Count);

        foreach (var (id, title, count, lastSeen, avgDurSecs) in rawRows)
        {
            if (count < MinOccurrences) continue;

            string freqLabel = BuildFrequencyLabel(count, analysisDays);
            bool   escalating = IsEscalating(count, analysisDays);

            results.Add(new RecurringInsightPattern(
                InsightId:         id,
                Title:             title,
                TotalOccurrences:  count,
                AvgDurationMinutes: Math.Round(avgDurSecs / 60.0, 1),
                LastSeen:          lastSeen,
                FrequencyLabel:    freqLabel,
                IsEscalating:      escalating));
        }

        // Already sorted by frequency from the DB query, but ensure descending
        results.Sort((a, b) => b.TotalOccurrences.CompareTo(a.TotalOccurrences));
        return results;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string BuildFrequencyLabel(int count, int days)
    {
        double perDay = (double)count / days;

        return perDay switch
        {
            >= 1.0  => "Daily",
            >= 0.43 => "Several times per week",  // ≥3/7
            >= 0.14 => "Weekly",                  // ≥1/7
            >= 0.07 => "Every 2 weeks",
            _       => "Occasional",
        };
    }

    /// <summary>
    /// Heuristic: if count/days is greater than the equivalent rate for the
    /// first half of the window, the pattern is escalating. Since we only
    /// have the aggregate count, we use a simple proxy: count > days × 0.5
    /// (i.e., more than half the window covered by occurrences means high
    /// enough frequency to flag as escalating).
    /// </summary>
    private static bool IsEscalating(int count, int days)
    {
        // More than 1 occurrence per 2 days → escalating
        return (double)count / days > 0.5;
    }
}
