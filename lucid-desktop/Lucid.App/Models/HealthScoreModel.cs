namespace Lucid.Models;

/// <summary>
/// Weighted health score for the system.
///
/// Weights (per intelligence-engine.md):
///   Security    30%
///   Stability   25%
///   Performance 20%
///   Storage     15%
///   Privacy     10%
///
/// Each category is 0–100. Overall is the weighted sum.
/// Phase 3: computed by the Intelligence Engine rules engine.
/// Phase 1: static mock values.
/// </summary>
public sealed record HealthScoreModel(
    int Overall,
    int Security,
    int Stability,
    int Performance,
    int Storage,
    int Privacy
)
{
    /// <summary>Human-readable label derived from the overall score.</summary>
    public string Label => Overall switch
    {
        >= 90 => "Excellent",
        >= 75 => "Good",
        >= 55 => "Fair",
        >= 35 => "Poor",
        _     => "Critical"
    };

    /// <summary>Hex color string for the overall score ring.</summary>
    public string ColorHex => Overall switch
    {
        >= 75 => "#57D68D",
        >= 55 => "#FFB347",
        _     => "#FF6B6B"
    };

    /// <summary>Weighted score computed from the five categories.</summary>
    public static HealthScoreModel Compute(
        int security, int stability, int performance, int storage, int privacy)
    {
        var overall = (int)(
            security    * 0.30 +
            stability   * 0.25 +
            performance * 0.20 +
            storage     * 0.15 +
            privacy     * 0.10);

        return new HealthScoreModel(overall, security, stability, performance, storage, privacy);
    }

    /// <summary>Static placeholder for Phase 1 before the rules engine is ready.</summary>
    public static HealthScoreModel Mock() =>
        Compute(security: 90, stability: 88, performance: 72, storage: 63, privacy: 85);
}
