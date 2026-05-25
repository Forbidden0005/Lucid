using Lucid.Services.Analytics;
using Lucid.Services.Learning;

namespace Lucid.Services.Simulation;

/// <summary>
/// Scores simulation confidence based on the quality and quantity of available data.
///
/// Three independent factors contribute to overall confidence:
///
///   1. Data Coverage (30%)
///      Derived from total telemetry rows and insight events.
///      More historical data = better trend signal = higher confidence.
///
///   2. Historical Basis (40%)
///      Derived from how long the machine has been monitored and the
///      consistency of observed trends.
///
///   3. Learning Data (30%)
///      Derived from whether the learning service has warm effectiveness
///      profiles for the actions involved in the scenario.
///
/// Overall confidence is the weighted sum, clamped to [30, 88].
/// Confidence above 88% is intentionally not achievable — the model
/// acknowledges real-world uncertainty even with perfect data.
///
/// All methods are deterministic and pure. No state, no I/O.
/// </summary>
public static class SimulationConfidenceScorer
{
    private const int MaxOverall         = 88;
    private const int MinOverall         = 30;

    // ── Data coverage thresholds ──────────────────────────────────────────────
    private const long RowsForFullDataConf   = 10_000;
    private const long RowsForMinDataConf    = 200;
    private const long EventsForFullInsight  = 100;

    // ── Historical basis thresholds ───────────────────────────────────────────
    private const int DaysForFullHistConf   = 21;
    private const int DaysForMinHistConf    = 3;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Produces a full <see cref="SimulationConfidenceScore"/> for the given scenario,
    /// incorporating all available data quality signals.
    /// </summary>
    public static SimulationConfidenceScore Score(
        SimulationScenarioType                                          scenario,
        HistoricalSummary?                                              summary,
        IReadOnlyDictionary<string, RecommendationEffectivenessProfile> profiles)
    {
        var factors = new List<string>();

        // ── Factor 1: Data coverage (0–100) ───────────────────────────────────
        int dataCoverage = ScoreDataCoverage(summary, factors);

        // ── Factor 2: Historical basis (0–100) ────────────────────────────────
        int historicalBasis = ScoreHistoricalBasis(summary, factors);

        // ── Factor 3: Learning data (0–100) ───────────────────────────────────
        int learningData = ScoreLearningData(scenario, profiles, factors);

        // ── Weighted overall ───────────────────────────────────────────────────
        int overall = (int)(dataCoverage * 0.30 + historicalBasis * 0.40 + learningData * 0.30);
        overall = Math.Clamp(overall, MinOverall, MaxOverall);

        string explanation = BuildExplanation(overall, summary, profiles, scenario);

        return new SimulationConfidenceScore(
            OverallPercent:        overall,
            DataCoveragePercent:   dataCoverage,
            HistoricalBasisPercent: historicalBasis,
            LearningDataPercent:   learningData,
            ExplanationText:       explanation,
            Factors:               factors);
    }

    // ── Factor scorers ────────────────────────────────────────────────────────

    private static int ScoreDataCoverage(HistoricalSummary? summary, List<string> factors)
    {
        if (summary is null)
        {
            factors.Add("No historical telemetry data available — confidence is minimal.");
            return 10;
        }

        // Scale from rows: 200 rows = 30%, 10,000 rows = 100%
        long rows = summary.TotalTelemetryRows;
        int rowScore = rows >= RowsForFullDataConf
            ? 100
            : rows <= RowsForMinDataConf
                ? 30
                : (int)(30 + 70.0 * (rows - RowsForMinDataConf) / (RowsForFullDataConf - RowsForMinDataConf));

        // Bonus from insight events
        long events    = summary.TotalInsightEvents;
        int eventBonus = events >= EventsForFullInsight ? 10 : (int)(10.0 * events / EventsForFullInsight);
        int coverage   = Math.Min(100, rowScore + eventBonus);

        if (rows < 500)
            factors.Add($"Limited telemetry history ({rows} samples) — accumulates with more usage.");
        else if (rows >= RowsForFullDataConf)
            factors.Add($"Strong telemetry coverage ({rows:N0} samples) supports reliable trend analysis.");
        else
            factors.Add($"Moderate telemetry coverage ({rows:N0} samples) available.");

        return coverage;
    }

    private static int ScoreHistoricalBasis(HistoricalSummary? summary, List<string> factors)
    {
        if (summary?.OldestDataPoint is null)
        {
            factors.Add("No historical baseline established yet.");
            return 20;
        }

        double ageDays = (DateTimeOffset.Now - summary.OldestDataPoint.Value).TotalDays;

        int ageScore = ageDays >= DaysForFullHistConf
            ? 100
            : ageDays <= DaysForMinHistConf
                ? 20
                : (int)(20 + 80.0 * (ageDays - DaysForMinHistConf) / (DaysForFullHistConf - DaysForMinHistConf));

        // Bonus: consistent trends improve confidence
        bool trendsConsistent = AreHealthScoresConsistent(summary);
        if (trendsConsistent)
        {
            ageScore = Math.Min(100, ageScore + 8);
            factors.Add("Trends are consistent across 7-day and 30-day windows.");
        }

        if (ageDays < DaysForMinHistConf)
            factors.Add($"Machine has only been monitored for {ageDays:F0} days — more time builds a stronger baseline.");
        else if (ageDays >= DaysForFullHistConf)
            factors.Add($"Machine has {ageDays:F0} days of monitoring history — strong basis for projections.");
        else
            factors.Add($"Machine has {ageDays:F0} days of monitoring history.");

        return ageScore;
    }

    private static int ScoreLearningData(
        SimulationScenarioType                                          scenario,
        IReadOnlyDictionary<string, RecommendationEffectivenessProfile> profiles,
        List<string>                                                    factors)
    {
        if (profiles.Count == 0)
        {
            factors.Add("No action effectiveness data recorded yet — projections use default estimates.");
            return 25;
        }

        // Check which actions are relevant to this scenario
        var relevantKeys = GetRelevantActionKeys(scenario);
        int warmProfiles = relevantKeys.Count(k => profiles.TryGetValue(k, out var p) && p.IsWarmEnough);

        if (warmProfiles == 0)
        {
            factors.Add("No prior runs of this action type on this machine to learn from.");
            return 30;
        }

        int score = Math.Min(100, 40 + warmProfiles * 25);

        var profileSamples = relevantKeys
            .Where(k => profiles.ContainsKey(k))
            .Sum(k => profiles[k].TotalAnalyzed);

        factors.Add($"Based on {warmProfiles} warm action profile{(warmProfiles > 1 ? "s" : "")} ({profileSamples} total recorded outcomes).");

        // Effectiveness quality bonus
        double avgEffectiveness = relevantKeys
            .Where(k => profiles.TryGetValue(k, out var p) && p.IsWarmEnough)
            .Average(k => profiles[k].EffectivenessRate);
        if (avgEffectiveness >= 0.6)
            factors.Add($"Historical actions of this type show {avgEffectiveness * 100:F0}% effectiveness on this machine.");

        return score;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool AreHealthScoresConsistent(HistoricalSummary summary)
    {
        // Consistent = both health scores point in the same trend direction
        return summary.SevenDayHealth.Trend == summary.ThirtyDayHealth.Trend
            || Math.Abs(summary.SevenDayHealth.Score - summary.ThirtyDayHealth.Score) < 10;
    }

    private static IReadOnlyList<string> GetRelevantActionKeys(SimulationScenarioType scenario) =>
        scenario switch
        {
            SimulationScenarioType.DisableStartupApps =>
                ["action.startup.disable-startup-app", "action.startup.backup-startup-state"],

            SimulationScenarioType.CleanupDisk =>
                ["action.disk.clean-temp-files", "action.disk.empty-recycle-bin",
                 "action.disk.clean-windows-update-cache", "action.disk.clean-delivery-optimization"],

            SimulationScenarioType.RepairWindows =>
                ["action.repair.sfc-scannow", "action.repair.dism-restore-health"],

            SimulationScenarioType.TerminateProcess =>
                ["action.process.terminate"],

            SimulationScenarioType.RestartSystem =>
                ["action.startup.backup-startup-state"],

            _ => [],
        };

    private static string BuildExplanation(
        int                    overall,
        HistoricalSummary?     summary,
        IReadOnlyDictionary<string, RecommendationEffectivenessProfile> profiles,
        SimulationScenarioType scenario)
    {
        string tier = overall switch
        {
            >= 75 => "Good",
            >= 55 => "Moderate",
            >= 40 => "Limited",
            _     => "Preliminary",
        };

        string dataNote = summary is null
            ? "No historical data available."
            : $"{summary.TotalTelemetryRows:N0} telemetry samples available.";

        string learningNote = profiles.Count == 0
            ? "No prior action outcomes recorded."
            : $"{profiles.Count} action profile{(profiles.Count > 1 ? "s" : "")} loaded from prior runs.";

        return $"{tier} confidence ({overall}%). {dataNote} {learningNote} " +
               "Estimates use linear extrapolation of observed trends and are not guarantees.";
    }
}
