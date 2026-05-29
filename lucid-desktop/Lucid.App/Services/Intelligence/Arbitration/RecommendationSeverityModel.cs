namespace Lucid.Services.Intelligence.Arbitration;

/// <summary>
/// Classifies a recommendation's operational severity for arbitration decisions.
///
/// Severity drives two arbitration behaviors:
///   1. Suppression threshold — only recommendations above the current
///      <see cref="SuppressBelow"/> level are surfaced to the user.
///   2. Deduplication window — higher severity recommendations have shorter
///      deduplication windows so they re-surface sooner.
/// </summary>
public enum RecommendationSeverity
{
    /// <summary>Cosmetic or minor improvement — nice to have.</summary>
    Cosmetic = 0,

    /// <summary>Maintenance recommendation — helpful but not urgent.</summary>
    Maintenance = 1,

    /// <summary>Performance optimization — measurable improvement available.</summary>
    Performance = 2,

    /// <summary>Stability concern — risk of degradation without action.</summary>
    Stability = 3,

    /// <summary>Critical issue — immediate action warranted.</summary>
    Critical = 4,
}

/// <summary>
/// Model that determines severity classification and suppression policy for
/// a recommendation based on its insight category and system state.
///
/// Stateless utility — all methods are pure functions.
/// </summary>
public static class RecommendationSeverityModel
{
    /// <summary>
    /// Classifies the severity of a recommendation based on its insight category
    /// and action type descriptor.
    /// </summary>
    public static RecommendationSeverity Classify(
        string insightCategory,
        string actionTypeDescriptor)
    {
        var cat = insightCategory.ToLowerInvariant();
        var act = actionTypeDescriptor.ToLowerInvariant();

        // Critical: forecasts with imminent trajectory
        if (cat.Contains("forecast") && (act.Contains("exhaust") || act.Contains("runaway")))
            return RecommendationSeverity.Critical;

        // Stability: thermal, runaway, or repair
        if (cat.Contains("thermal") || act.Contains("repair") || cat.Contains("stability"))
            return RecommendationSeverity.Stability;

        // Performance: CPU, RAM, startup
        if (cat.Contains("cpu") || cat.Contains("ram") || cat.Contains("startup") ||
            cat.Contains("performance") || act.Contains("startup"))
            return RecommendationSeverity.Performance;

        // Maintenance: storage, cleanup
        if (cat.Contains("storage") || cat.Contains("disk") || act.Contains("clean") ||
            act.Contains("temp") || act.Contains("recycle"))
            return RecommendationSeverity.Maintenance;

        // Default: maintenance
        return RecommendationSeverity.Maintenance;
    }

    /// <summary>
    /// Returns the deduplication window for a severity tier.
    /// Recommendations seen within this window are not re-shown.
    /// </summary>
    public static TimeSpan DeduplicationWindow(RecommendationSeverity severity) =>
        severity switch
        {
            RecommendationSeverity.Critical    => TimeSpan.FromMinutes(5),
            RecommendationSeverity.Stability   => TimeSpan.FromMinutes(15),
            RecommendationSeverity.Performance => TimeSpan.FromMinutes(30),
            RecommendationSeverity.Maintenance => TimeSpan.FromHours(2),
            RecommendationSeverity.Cosmetic    => TimeSpan.FromHours(8),
            _                                  => TimeSpan.FromHours(1),
        };
}
