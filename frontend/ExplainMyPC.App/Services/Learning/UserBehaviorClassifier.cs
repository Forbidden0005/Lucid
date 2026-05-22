namespace ExplainMyPC.Services.Learning;

// ── Interface ─────────────────────────────────────────────────────────────────

/// <summary>
/// Converts a <see cref="PersonalizationProfile"/> into a human-readable
/// <see cref="OperationalStyleReport"/> for display on the Dashboard.
/// Pure function — stateless, no side effects.
/// </summary>
public interface IUserBehaviorClassifier
{
    /// <summary>
    /// Classifies the user's operational style and builds a report for display.
    /// Returns a "warming up" report when the profile is cold-start.
    /// </summary>
    OperationalStyleReport Classify(PersonalizationProfile profile);
}

// ── Implementation ─────────────────────────────────────────────────────────────

public sealed class UserBehaviorClassifier : IUserBehaviorClassifier
{
    public OperationalStyleReport Classify(PersonalizationProfile profile)
    {
        if (!profile.IsWarmEnough)
        {
            return new OperationalStyleReport
            {
                Style          = UserOperationalStyle.Unknown,
                StyleLabel     = "Learning your style",
                StyleDescription = "ExplainMyPC is observing how you interact with " +
                                   "recommendations. Check back after a few actions.",
                StyleGlyph     = "", // Streaming/learning glyph
                StyleColor     = "#6B7280",
                PersonalInsight = "Accept or dismiss a few recommendations to help " +
                                  "ExplainMyPC adapt to how you work.",
                SampleCount    = profile.TotalRecords,
            };
        }

        return profile.Style switch
        {
            UserOperationalStyle.Reactive     => BuildReport(profile,
                label:       "Reactive User",
                description: "You tend to act on recommendations when a problem has already surfaced. " +
                             "ExplainMyPC will surface warning-level items more prominently for you.",
                glyph:       "", // Alert bell
                color:       "#F59E0B",
                insight:     BuildInsight(profile)),

            UserOperationalStyle.Proactive    => BuildReport(profile,
                label:       "Proactive User",
                description: "You often act on recommendations before conditions escalate to warnings. " +
                             "ExplainMyPC will emphasize early forecasts and trend findings.",
                glyph:       "", // Play / ahead
                color:       "#10B981",
                insight:     BuildInsight(profile)),

            UserOperationalStyle.HandsOff     => BuildReport(profile,
                label:       "Monitoring Mode",
                description: "You prefer to observe rather than act frequently. " +
                             "ExplainMyPC will focus on the highest-impact items only.",
                glyph:       "", // Eye / view
                color:       "#6B7280",
                insight:     BuildInsight(profile)),

            UserOperationalStyle.Investigative => BuildReport(profile,
                label:       "Investigative User",
                description: "You read explanations carefully before acting. " +
                             "ExplainMyPC will provide more context and reasoning in recommendations.",
                glyph:       "", // Search
                color:       "#8B5CF6",
                insight:     BuildInsight(profile)),

            _ => BuildReport(profile,
                label:       "Balanced User",
                description: "You accept recommendations at a steady rate across categories. " +
                             "ExplainMyPC will maintain a balanced recommendation mix.",
                glyph:       "", // Equal / balance
                color:       "#4E9FFF",
                insight:     BuildInsight(profile)),
        };
    }

    private static OperationalStyleReport BuildReport(
        PersonalizationProfile profile,
        string label, string description, string glyph, string color, string insight) =>
        new()
        {
            Style            = profile.Style,
            StyleLabel       = label,
            StyleDescription = description,
            StyleGlyph       = glyph,
            StyleColor       = color,
            PersonalInsight  = insight,
            SampleCount      = profile.TotalRecords,
        };

    private static string BuildInsight(PersonalizationProfile profile)
    {
        if (profile.CategoryAcceptanceRates.Count == 0) return string.Empty;

        // Find most and least accepted categories
        var sorted = profile.CategoryAcceptanceRates
            .OrderByDescending(kv => kv.Value)
            .ToList();

        var best  = sorted.First();
        var worst = sorted.Last();

        if (sorted.Count == 1)
            return $"You accept {FormatCategory(best.Key)} actions " +
                   $"{best.Value:0%} of the time.";

        if (best.Value > worst.Value + 0.3)
            return $"You tend to act on {FormatCategory(best.Key)} recommendations " +
                   $"but rarely on {FormatCategory(worst.Key)} items.";

        return $"You engage consistently across categories, " +
               $"most often with {FormatCategory(best.Key)} recommendations.";
    }

    private static string FormatCategory(string category) => category.ToLowerInvariant() switch
    {
        "disk"    => "disk cleanup",
        "startup" => "startup",
        "process" => "process",
        "repair"  => "repair",
        "storage" => "storage",
        "network" => "network",
        "cpu"     => "CPU",
        "ram"     => "memory",
        "security" => "security",
        _ => category,
    };
}
