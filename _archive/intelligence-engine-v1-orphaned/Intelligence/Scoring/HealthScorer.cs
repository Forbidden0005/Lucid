using ExplainMyPC.Intelligence.Models;
using ExplainMyPC.Intelligence.Rules;
using ExplainMyPC.Models;

namespace ExplainMyPC.Intelligence.Scoring;

/// <summary>
/// Translates fired rules into a HealthScoreModel.
///
/// Algorithm per category:
///   1. Collect all issues that affect the category (some categories, e.g.
///      Startup, spread their deduction across two score dimensions).
///   2. Sort issues by severity descending so the worst issue applies first.
///   3. Apply diminishing deductions: each subsequent issue in the same category
///      contributes less (60% → 36% → 22% of the severity base value).
///      This prevents two minor issues from appearing worse than one major one.
///   4. Clamp each category score to [0, 100].
///   5. Compute overall as the weighted sum of category scores per architecture.md.
///
/// Severity → base deduction (points out of 100):
///   Informational  →  0   (never penalises)
///   Low            →  8
///   Moderate       → 20
///   High           → 35
///   Critical       → 55
///
/// Category → score dimension mapping:
///   Performance  → PerformanceScore
///   Memory       → PerformanceScore   (memory pressure directly impacts perf)
///   Startup      → 50% Performance + 50% Stability
///   Thermal      → StabilityScore
///   Storage      → StorageScore
///   Security     → SecurityScore
///   Stability    → StabilityScore
///   Privacy      → PrivacyScore
///
/// Baseline when no rules fire:
///   All categories default to 100. The engine does not apply phantom penalties
///   for categories we have not yet scanned — that would be dishonest.
///
/// Weights (from intelligence-engine.md and architecture.md):
///   Security 30%  •  Stability 25%  •  Performance 20%  •  Storage 15%  •  Privacy 10%
/// </summary>
public static class HealthScorer
{
    // ── Severity → point deduction ────────────────────────────────────────────
    private static readonly IReadOnlyDictionary<IssueSeverity, double> BaseDeduction =
        new Dictionary<IssueSeverity, double>
        {
            [IssueSeverity.Informational] =  0,
            [IssueSeverity.Low]           =  8,
            [IssueSeverity.Moderate]      = 20,
            [IssueSeverity.High]          = 35,
            [IssueSeverity.Critical]      = 55,
        };

    // Diminishing return factors applied to the 2nd, 3rd, 4th+ issues in a category.
    private static readonly double[] DiminishingFactors = [1.0, 0.60, 0.36, 0.22];

    // ── Category → score dimension(s) ─────────────────────────────────────────
    // Each entry is (scoreKey, weight). Startup splits evenly across two dimensions.
    private static IEnumerable<(ScoreDimension Dimension, double Weight)> DimensionsFor(IssueCategory cat) =>
        cat switch
        {
            IssueCategory.Performance => [(ScoreDimension.Performance, 1.0)],
            IssueCategory.Memory      => [(ScoreDimension.Performance, 1.0)],
            IssueCategory.Startup     => [(ScoreDimension.Performance, 0.5), (ScoreDimension.Stability, 0.5)],
            IssueCategory.Thermal     => [(ScoreDimension.Stability,   1.0)],
            IssueCategory.Storage     => [(ScoreDimension.Storage,     1.0)],
            IssueCategory.Security    => [(ScoreDimension.Security,    1.0)],
            IssueCategory.Stability   => [(ScoreDimension.Stability,   1.0)],
            IssueCategory.Privacy     => [(ScoreDimension.Privacy,     1.0)],
            _                         => [(ScoreDimension.Performance, 1.0)],
        };

    public static HealthScoreModel Compute(IReadOnlyList<RuleResult> fired)
    {
        // Accumulate deductions per dimension.
        var deductions = new Dictionary<ScoreDimension, List<double>>
        {
            [ScoreDimension.Security]    = [],
            [ScoreDimension.Stability]   = [],
            [ScoreDimension.Performance] = [],
            [ScoreDimension.Storage]     = [],
            [ScoreDimension.Privacy]     = [],
        };

        foreach (var rule in fired)
        {
            double basePoints = BaseDeduction[rule.Severity];
            if (basePoints <= 0) continue;

            foreach (var (dim, weight) in DimensionsFor(rule.Category))
                deductions[dim].Add(basePoints * weight);
        }

        // Apply diminishing returns and compute each category score.
        int security    = Score(deductions[ScoreDimension.Security]);
        int stability   = Score(deductions[ScoreDimension.Stability]);
        int performance = Score(deductions[ScoreDimension.Performance]);
        int storage     = Score(deductions[ScoreDimension.Storage]);
        int privacy     = Score(deductions[ScoreDimension.Privacy]);

        return HealthScoreModel.Compute(security, stability, performance, storage, privacy);
    }

    private static int Score(List<double> penalties)
    {
        if (penalties.Count == 0) return 100;

        // Sort descending so the worst penalty absorbs the full factor.
        penalties.Sort((a, b) => b.CompareTo(a));

        double total = 0;
        for (int i = 0; i < penalties.Count; i++)
        {
            double factor = i < DiminishingFactors.Length
                ? DiminishingFactors[i]
                : DiminishingFactors[^1];
            total += penalties[i] * factor;
        }

        return (int)Math.Clamp(Math.Round(100.0 - total), 0, 100);
    }

    private enum ScoreDimension { Security, Stability, Performance, Storage, Privacy }
}
