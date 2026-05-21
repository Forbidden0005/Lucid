using ExplainMyPC.Services.Learning;

namespace ExplainMyPC.Services.Watchtower;

/// <summary>
/// Produces ranked <see cref="InterventionSuggestion"/> records by combining
/// active watchtower alerts with remediation effectiveness profiles from the
/// learning service.
///
/// The planner ONLY suggests — it never triggers automatic execution.
/// Suggestions are ranked by: severity × effectiveness × confidence.
/// </summary>
public static class InterventionPlanner
{
    /// <summary>
    /// Builds a ranked list of intervention suggestions from the provided alerts
    /// and effectiveness profiles. At most 5 suggestions are returned.
    /// </summary>
    public static IReadOnlyList<InterventionSuggestion> Plan(
        IReadOnlyList<WatchtowerAlert>  alerts,
        IReadOnlyDictionary<string, RecommendationEffectivenessProfile> profiles)
    {
        var suggestions = new List<InterventionSuggestion>();
        var addedKeys   = new HashSet<string>(StringComparer.Ordinal);

        foreach (var alert in alerts)
        {
            if (alert.LinkedActionKey is null) continue;
            if (!addedKeys.Add(alert.LinkedActionKey)) continue; // deduplicate

            var profile = profiles.GetValueOrDefault(alert.LinkedActionKey);
            var (effectivenessLabel, bonus) = ScoreEffectiveness(profile);

            // Base score: severity drives priority
            var baseScore = alert.Severity switch
            {
                WatchtowerAlertSeverity.Elevated => 100,
                WatchtowerAlertSeverity.Warning  => 75,
                WatchtowerAlertSeverity.Advisory => 50,
                _                                => 25,
            };

            var finalScore = baseScore + bonus;
            var confidence = alert.ConfidencePercent;

            // Reduce confidence when learning data is sparse
            if (profile is not null && !profile.IsWarmEnough)
                confidence = Math.Max(30, confidence - 15);

            suggestions.Add(new InterventionSuggestion(
                SuggestionId:       $"intervention.{alert.AlertId}",
                Title:              BuildTitle(alert),
                Rationale:          BuildRationale(alert, profile),
                ActionKey:          alert.LinkedActionKey,
                EffectivenessLabel: effectivenessLabel,
                Priority:           finalScore,
                ConfidencePercent:  confidence));
        }

        // Add a general navigation suggestion when no linked actions exist
        if (suggestions.Count == 0 && alerts.Count > 0)
        {
            suggestions.Add(new InterventionSuggestion(
                SuggestionId:       "intervention.general.review-insights",
                Title:              "Review active intelligence findings",
                Rationale:          "The Intelligence page may have actionable findings related to the observed patterns.",
                ActionKey:          null,
                EffectivenessLabel: "General guidance",
                Priority:           30,
                ConfidencePercent:  60));
        }

        // Sort by priority descending, take top 5
        suggestions.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        var rank = 1;
        return suggestions
            .Take(5)
            .Select(s => s with { Priority = rank++ })
            .ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (string label, int bonus) ScoreEffectiveness(
        RecommendationEffectivenessProfile? profile)
    {
        if (profile is null || !profile.IsWarmEnough)
            return ("Limited historical data on this machine", 0);

        return profile.EffectivenessRate switch
        {
            >= 0.70 => ("Historically effective on this machine", +20),
            >= 0.45 => ("Mixed results on this machine",          +5),
            _       => ("Low effectiveness on this machine",      -10),
        };
    }

    private static string BuildTitle(WatchtowerAlert alert) =>
        alert.ActionHint ?? $"Address: {alert.Title}";

    private static string BuildRationale(
        WatchtowerAlert alert,
        RecommendationEffectivenessProfile? profile)
    {
        var rationale = alert.Explanation;

        if (profile is { IsWarmEnough: true } && profile.EffectivenessRate >= 0.45)
            rationale += $" This action has shown positive results on this machine " +
                         $"in {profile.ImprovedCount + profile.PartiallyImprovedCount} of " +
                         $"{profile.TotalAnalyzed} previous uses.";

        return rationale;
    }
}
