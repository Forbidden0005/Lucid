using ExplainMyPC.Services.Learning;

namespace ExplainMyPC.Services.Intelligence;

// ── Models ─────────────────────────────────────────────────────────────────────

/// <summary>
/// A recommendation action ranked by its predicted net impact for this machine.
/// Produced by <see cref="IGlobalRecommendationPrioritizer.Rank"/>.
/// </summary>
public sealed record PrioritizedAction(
    /// <summary>The underlying recommended action.</summary>
    SystemAction  Action,

    /// <summary>The insight that generated this action.</summary>
    SystemInsight SourceInsight,

    /// <summary>Composite priority score in [0, 1]. Higher = more impactful / easier / proven.</summary>
    double        PriorityScore,

    /// <summary>Human-readable priority tier: "High priority" / "Medium priority" / "Lower priority".</summary>
    string        PriorityLabel,

    /// <summary>
    /// Effectiveness label from the learning profile, e.g. "Historically effective on this machine"
    /// or "No outcome data yet" when the profile is cold.
    /// </summary>
    string        EffectivenessLabel,

    /// <summary>
    /// Transparent breakdown of how the score was computed.
    /// All components are in [0, 1] unless noted.
    /// </summary>
    ScoreBreakdown ScoreBreakdown,

    /// <summary>
    /// Human-readable explanation of why this action was ranked at this level.
    /// Shown in the "Why Recommended" section on Dashboard and Insights pages.
    /// May be empty until the explanation service has been wired in.
    /// </summary>
    string WhyRecommended);

// ── Interface ─────────────────────────────────────────────────────────────────

/// <summary>
/// Ranks recommended actions by predicted net benefit, combining action impact,
/// required effort, insight severity, machine-specific learning data, and
/// adaptive personalization from user interaction history.
///
/// This is a pure function — stateless, no side effects, no background work.
/// Call <see cref="Rank"/> on demand whenever the active insight set changes.
/// </summary>
public interface IGlobalRecommendationPrioritizer
{
    /// <summary>
    /// Returns a ranked list of actions sourced from <paramref name="insights"/>,
    /// ordered by priority score descending (most impactful first).
    ///
    /// Deduplicates by <see cref="SystemAction.Id"/> — each action appears once
    /// even if recommended by multiple insights simultaneously.
    /// </summary>
    IReadOnlyList<PrioritizedAction> Rank(
        IReadOnlyList<SystemInsight>  insights,
        IRemediationLearningService   learning);

    /// <summary>
    /// Personalization-aware overload.
    /// Applies category preference weights and alert fatigue suppression from
    /// the user's interaction history on top of the base scoring formula.
    /// </summary>
    IReadOnlyList<PrioritizedAction> Rank(
        IReadOnlyList<SystemInsight>        insights,
        IRemediationLearningService         learning,
        PersonalizationProfile?             profile,
        IAlertFatigueManager?               fatigueManager,
        IRecommendationExplanationService?  explanationService);
}

// ── Implementation ─────────────────────────────────────────────────────────────

/// <summary>
/// Deterministic, local-only recommendation prioritizer.
///
/// Scoring formula (composite, 0–1):
/// <code>
///   impactScore    = (int)Action.Impact / 3.0
///   effortFactor   = 1 − ((int)Action.Effort / 4.0)
///   severityBoost  = Warning → 1.20 | Recommendation → 1.00 | Info → 0.80
///   effectiveness  = learning EffectivenessRate (warm) OR 0.65 neutral prior (cold)
///   personalization = category weight from profile (1.0 when cold-start)
///   fatigue        = AlertFatigueManager.GetFatigueFactor() (1.0 when no manager)
///   score = clamp(impactScore × effortFactor × severityBoost × effectiveness
///                 × personalization × fatigue, 0, 1)
/// </code>
///
/// Priority label thresholds:
///   ≥ 0.75 → "High priority"
///   ≥ 0.45 → "Medium priority"
///         → "Lower priority"
/// </summary>
public sealed class GlobalRecommendationPrioritizer : IGlobalRecommendationPrioritizer
{
    /// <summary>
    /// Neutral effectiveness prior used when the learning profile has fewer than
    /// two analyzed executions.
    /// </summary>
    private const double NeutralEffectiveness = 0.65;

    // ── Base rank (no personalization) ────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<PrioritizedAction> Rank(
        IReadOnlyList<SystemInsight>  insights,
        IRemediationLearningService   learning) =>
        Rank(insights, learning,
             profile:            null,
             fatigueManager:     null,
             explanationService: null);

    // ── Personalization-aware rank ─────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<PrioritizedAction> Rank(
        IReadOnlyList<SystemInsight>        insights,
        IRemediationLearningService         learning,
        PersonalizationProfile?             profile,
        IAlertFatigueManager?               fatigueManager,
        IRecommendationExplanationService?  explanationService)
    {
        var seen   = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<PrioritizedAction>();

        var tolerance = profile?.AlertTolerance ?? AlertTolerance.Medium;

        foreach (var insight in insights)
        {
            foreach (var action in insight.RecommendedActions)
            {
                if (!seen.Add(action.Id)) continue;

                double effectiveness = GetEffectiveness(action.Id, learning);
                double personalization = GetPersonalizationWeight(action.Id, profile);
                double fatigue = fatigueManager?.GetFatigueFactor(action.Id, tolerance) ?? 1.0;
                string fatigueNote = fatigueManager?.GetFatigueNote(action.Id, tolerance) ?? string.Empty;

                var breakdown = ComputeBreakdown(action, insight.Severity,
                    effectiveness, personalization, fatigue);

                string priorityLabel      = PriorityLabel(breakdown.FinalScore);
                string effectivenessLabel = GetEffectivenessLabel(action.Id, learning);

                var prioritized = new PrioritizedAction(
                    Action:             action,
                    SourceInsight:      insight,
                    PriorityScore:      breakdown.FinalScore,
                    PriorityLabel:      priorityLabel,
                    EffectivenessLabel: effectivenessLabel,
                    ScoreBreakdown:     breakdown,
                    WhyRecommended:     string.Empty); // populated below

                // Attach explanation if service is available
                if (explanationService is not null)
                {
                    var explanation = explanationService.Explain(prioritized, profile, fatigueNote);
                    prioritized = prioritized with { WhyRecommended = explanation.FullText };
                }

                result.Add(prioritized);
            }
        }

        result.Sort((a, b) => b.PriorityScore.CompareTo(a.PriorityScore));
        return result.AsReadOnly();
    }

    // ── Scoring helpers ────────────────────────────────────────────────────────

    private static ScoreBreakdown ComputeBreakdown(
        SystemAction    action,
        InsightSeverity severity,
        double          effectiveness,
        double          personalization,
        double          fatigue)
    {
        double impact  = (int)action.Impact / 3.0;
        double effort  = 1.0 - (int)action.Effort / 4.0;
        double boost   = SeverityBoost(severity);

        double final = Math.Clamp(
            impact * effort * boost * effectiveness * personalization * fatigue,
            0.0, 1.0);

        return new ScoreBreakdown
        {
            ImpactComponent         = impact,
            EffortComponent         = effort,
            SeverityBoost           = boost,
            EffectivenessWeight     = effectiveness,
            PersonalizationMultiplier = personalization,
            FatigueFactor           = fatigue,
            FinalScore              = final,
        };
    }

    private static double SeverityBoost(InsightSeverity severity) => severity switch
    {
        InsightSeverity.Warning        => 1.20,
        InsightSeverity.Recommendation => 1.00,
        InsightSeverity.Info           => 0.80,
        _                              => 1.00,
    };

    private static double GetEffectiveness(
        string                      actionId,
        IRemediationLearningService learning)
    {
        var profile = learning.GetProfile(actionId);
        return profile is { IsWarmEnough: true }
            ? profile.EffectivenessRate
            : NeutralEffectiveness;
    }

    private static double GetPersonalizationWeight(
        string                  actionId,
        PersonalizationProfile? profile)
    {
        if (profile is null || !profile.IsWarmEnough) return 1.0;

        string category = ExtractCategory(actionId);
        return profile.CategoryWeights.TryGetValue(category, out double w) ? w : 1.0;
    }

    private static string PriorityLabel(double score) => score switch
    {
        >= 0.75 => "High priority",
        >= 0.45 => "Medium priority",
        _       => "Lower priority",
    };

    private static string GetEffectivenessLabel(
        string                      actionId,
        IRemediationLearningService learning)
    {
        var profile = learning.GetProfile(actionId);
        if (profile is null || !profile.IsWarmEnough) return "No outcome data yet";
        return profile.EffectivenessLabel;
    }

    private static string ExtractCategory(string actionId)
    {
        var parts = actionId.Split('.');
        return parts.Length >= 2 ? parts[1] : actionId;
    }
}
