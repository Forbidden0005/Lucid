using Lucid.Services.Intelligence.Arbitration;
using Lucid.Services.Interaction.Recommendations;

namespace Lucid.Services.Interaction.Attention;

/// <summary>
/// Ranks notifications and recommendations by interrupt priority.
///
/// The priority engine answers:
///   "Of all the things Lucid wants to surface right now, what should surface first?"
///
/// Priority factors (in order):
///   1. Severity — Critical/Stability outrank Performance/Maintenance/Cosmetic
///   2. Urgency flag — marked urgent by the presentation layer
///   3. Freshness — new items outrank stale items
///   4. Fatigue — items with high fatigue scores are deprioritized
///
/// The output is a ranked list used by AttentionCoordinator to gate
/// which items are actually allowed to interrupt.
///
/// Thread-safe: stateless and pure.
/// </summary>
public static class NotificationPriorityEngine
{
    /// <summary>
    /// Ranks a list of unified recommendations by interrupt priority.
    /// Highest priority first.
    /// </summary>
    public static IReadOnlyList<UnifiedRecommendation> Rank(
        IReadOnlyList<UnifiedRecommendation> recommendations)
    {
        return recommendations
            .Where(r => r.IsActive)
            .OrderByDescending(r => GetPriorityScore(r))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Returns the interrupt priority score for a single recommendation.
    /// Higher = should interrupt sooner.
    /// </summary>
    public static double GetPriorityScore(UnifiedRecommendation recommendation)
    {
        double score = 0;

        // Severity is the dominant factor.
        score += (double)recommendation.Severity * 20.0;

        // Urgency flag adds a moderate boost.
        if (recommendation.Severity >= RecommendationSeverity.Stability)
            score += 10.0;

        // Priority score from arbitration.
        score += recommendation.PriorityScore * 5.0;

        // Fatigue reduces priority (IsFatigued means score hit ≤ 0.5 factor).
        if (recommendation.IsFatigued)
            score *= 0.5;

        // Age penalty: newer items are more relevant.
        var ageMinutes = (DateTime.UtcNow - recommendation.AssembledAt).TotalMinutes;
        score -= Math.Min(ageMinutes * 0.1, 10.0);

        return Math.Max(score, 0);
    }

    /// <summary>
    /// Returns true when a recommendation's priority warrants an interrupt
    /// at the given workload pressure level.
    /// </summary>
    public static bool ShouldInterrupt(
        UnifiedRecommendation recommendation,
        bool                  isHighWorkload)
    {
        // During high workload, only Stability/Critical can interrupt.
        if (isHighWorkload)
            return recommendation.Severity >= RecommendationSeverity.Stability &&
                   !recommendation.IsFatigued;

        // Normal conditions: Performance and above can interrupt.
        return recommendation.Severity >= RecommendationSeverity.Performance &&
               !recommendation.IsFatigued;
    }
}
