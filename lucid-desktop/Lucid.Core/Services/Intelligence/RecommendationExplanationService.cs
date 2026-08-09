using Lucid.Services.Learning;

namespace Lucid.Services.Intelligence;

// ── Interface ─────────────────────────────────────────────────────────────────

/// <summary>
/// Generates human-readable explanations for why a recommendation was ranked
/// at its current priority level.
///
/// Every explanation is transparent:
///   - Primary reason: impact, effort, and severity
///   - Personalization reason: how the user's history influenced the rank
///   - Effectiveness reason: what the machine's own history shows
///   - Fatigue note: when an action has been shown many times without acceptance
///
/// Local-only. Deterministic. Explainable.
/// </summary>
public interface IRecommendationExplanationService
{
    /// <summary>
    /// Builds a full <see cref="RecommendationExplanation"/> for the given action,
    /// incorporating the score breakdown, personalization profile, and effectiveness profile.
    /// </summary>
    RecommendationExplanation Explain(
        PrioritizedAction           action,
        PersonalizationProfile?     profile,
        string                      fatigueNote);
}

// ── Implementation ─────────────────────────────────────────────────────────────

public sealed class RecommendationExplanationService : IRecommendationExplanationService
{
    public RecommendationExplanation Explain(
        PrioritizedAction       action,
        PersonalizationProfile? profile,
        string                  fatigueNote)
    {
        var bd = action.ScoreBreakdown;

        string primary         = BuildPrimaryReason(action);
        string personalization = BuildPersonalizationReason(action, profile, bd);
        string effectiveness   = BuildEffectivenessReason(action);

        // Compose full text from non-empty parts
        var parts = new List<string>(4);
        if (!string.IsNullOrEmpty(primary))         parts.Add(primary);
        if (!string.IsNullOrEmpty(personalization)) parts.Add(personalization);
        if (!string.IsNullOrEmpty(effectiveness))   parts.Add(effectiveness);
        if (!string.IsNullOrEmpty(fatigueNote))     parts.Add(fatigueNote + ".");

        return new RecommendationExplanation
        {
            ActionKey            = action.Action.Id,
            PrimaryReason        = primary,
            PersonalizationReason = personalization,
            EffectivenessReason  = effectiveness,
            FatigueNote          = fatigueNote,
            FullText             = string.Join(" ", parts),
        };
    }

    // ── Reason builders ───────────────────────────────────────────────────────

    private static string BuildPrimaryReason(PrioritizedAction action)
    {
        var bd = action.ScoreBreakdown;
        string impact  = ImpactLabel(bd.ImpactComponent);
        string effort  = EffortLabel(bd.EffortComponent);
        string severity = action.SourceInsight.Severity.ToString().ToLowerInvariant();

        return $"Ranked {action.PriorityLabel.ToLowerInvariant()} because this is a {impact} action " +
               $"with {effort} and is linked to a {severity}-level finding.";
    }

    private static string BuildPersonalizationReason(
        PrioritizedAction action,
        PersonalizationProfile? profile,
        ScoreBreakdown bd)
    {
        if (profile is null || !profile.IsWarmEnough) return string.Empty;

        string category = ExtractCategory(action.Action.Id);
        if (!profile.CategoryWeights.TryGetValue(category, out double weight))
            return string.Empty;

        return weight switch
        {
            > 1.2 => $"Boosted because you've accepted {category} recommendations " +
                     $"{profile.CategoryAcceptanceRates.GetValueOrDefault(category):0%} of the time.",
            < 0.8 => $"De-prioritized because you've accepted {category} recommendations " +
                     $"only {profile.CategoryAcceptanceRates.GetValueOrDefault(category):0%} of the time.",
            _     => string.Empty,
        };
    }

    private static string BuildEffectivenessReason(PrioritizedAction action)
    {
        if (string.IsNullOrEmpty(action.EffectivenessLabel)) return string.Empty;
        if (action.EffectivenessLabel == "No outcome data yet") return string.Empty;

        return action.EffectivenessLabel + ".";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ImpactLabel(double impactComponent) => impactComponent switch
    {
        >= 0.9  => "very high-impact",
        >= 0.55 => "high-impact",
        >= 0.25 => "moderate-impact",
        _       => "low-impact",
    };

    private static string EffortLabel(double effortComponent) => effortComponent switch
    {
        >= 0.9  => "near-zero effort",
        >= 0.65 => "low effort",
        >= 0.4  => "moderate effort",
        _       => "higher effort",
    };

    private static string ExtractCategory(string actionId)
    {
        // e.g. "action.disk.clean-temp-files" → "disk"
        var parts = actionId.Split('.');
        return parts.Length >= 2 ? parts[1] : actionId;
    }
}
