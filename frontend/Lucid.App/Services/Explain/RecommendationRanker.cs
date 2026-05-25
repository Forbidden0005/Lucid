using Lucid.Services.Intelligence;

namespace Lucid.Services.Explain;

/// <summary>
/// Collects all <see cref="SystemAction"/> instances from active insight findings,
/// de-duplicates by action ID, and ranks them into a prioritized list of
/// <see cref="RecommendationReasoning"/> records.
///
/// Ranking formula (higher score = higher priority):
///   score = (impact × 30) + (confidence ÷ 2) + (reversible ? 15 : 0) − (risk × 8)
///
/// Where:
///   impact:     ActionImpact enum value × 30  → 0, 30, 60, 90
///   confidence: insight ConfidencePercent ÷ 2  → 0–50
///   reversible: +15 if action can be rolled back
///   risk:       ActionRisk enum value × 8      → 0, 8, 16, 24
///
/// This scoring deliberately:
///   • Favors reversible actions (safe to recommend)
///   • Penalizes high-risk actions (require more caution)
///   • Weights expected impact heavily (the user wants effective fixes)
///   • Incorporates confidence (uncertain findings drive lower-priority actions)
/// </summary>
internal static class RecommendationRanker
{
    internal static IReadOnlyList<RecommendationReasoning> Rank(
        IReadOnlyList<SystemInsight> insights)
    {
        var seen    = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<RecommendationReasoning>();

        // Process warnings first (highest priority), then recommendations
        foreach (var insight in insights
            .Where(i => i.Id != "system.healthy")
            .OrderByDescending(i => (int)i.Severity)
            .ThenByDescending(i => i.ConfidencePercent))
        {
            foreach (var action in insight.RecommendedActions)
            {
                if (!seen.Add(action.Id)) continue; // first occurrence wins

                results.Add(new RecommendationReasoning(
                    Action:            action,
                    WhyItMatters:      BuildWhyText(insight, action),
                    ExpectedBenefit:   BuildBenefitText(action),
                    EstimatedTime:     EffortLabel(action.Effort),
                    ConfidencePercent: insight.ConfidencePercent,
                    CanRollback:       action.IsReversible,
                    RankScore:         ComputeScore(action, insight.ConfidencePercent)));
            }
        }

        return results
            .OrderByDescending(r => r.RankScore)
            .ToList();
    }

    // ── Scoring ───────────────────────────────────────────────────────────────

    private static int ComputeScore(SystemAction action, int confidencePct)
    {
        int impactPts      = (int)action.Impact * 30;
        int confidencePts  = confidencePct / 2;
        int reversibleBonus = action.IsReversible ? 15 : 0;
        int riskPenalty    = (int)action.Risk * 8;

        return impactPts + confidencePts + reversibleBonus - riskPenalty;
    }

    // ── Text builders ─────────────────────────────────────────────────────────

    private static string BuildWhyText(SystemInsight insight, SystemAction action)
    {
        var finding = insight.Title.Length > 0
            ? char.ToLowerInvariant(insight.Title[0]) + insight.Title[1..]
            : insight.Title;

        return $"Recommended because {finding}. {action.Description}";
    }

    private static string BuildBenefitText(SystemAction action) => action.Impact switch
    {
        ActionImpact.Significant => "Expected to substantially resolve the issue",
        ActionImpact.High        => "Expected to noticeably improve the affected metric",
        ActionImpact.Moderate    => "Expected to provide a meaningful improvement",
        ActionImpact.Low         => "Helps identify or partially address the underlying condition",
        _                        => "Expected to improve system conditions",
    };

    private static string EffortLabel(ActionEffort effort) => effort switch
    {
        ActionEffort.Seconds    => "< 1 min",
        ActionEffort.FewMinutes => "2–5 min",
        ActionEffort.TenMinutes => "~10 min",
        _                       => "A few minutes",
    };
}
