namespace Lucid.Services.Reasoning.Cognitive;

/// <summary>
/// Discretized confidence level for an operational inference or recommendation.
///
/// Lucid must NEVER present speculation as certainty. Every conclusion is
/// labeled with its epistemic state so users can calibrate trust appropriately.
///
/// Maps from continuous <see cref="ConfidenceScore.Value"/> (0–1) to a human-readable
/// tier using <see cref="ConfidenceLevel.FromScore"/>.
/// </summary>
public enum ConfidenceLevel
{
    /// <summary>
    /// Score ≥ 0.90. Multiple independent evidence sources agree.
    /// Language: "Lucid has detected…", "the system shows…"
    /// </summary>
    Certain = 4,

    /// <summary>
    /// Score ≥ 0.70. Strong evidence, minor ambiguity.
    /// Language: "Lucid is confident that…", "evidence strongly suggests…"
    /// </summary>
    High = 3,

    /// <summary>
    /// Score ≥ 0.50. Reasonable evidence but notable uncertainty.
    /// Language: "Lucid believes…", "data suggests…"
    /// </summary>
    Medium = 2,

    /// <summary>
    /// Score ≥ 0.30. Weak or limited evidence; pattern is possible but unclear.
    /// Language: "Lucid has noticed…", "this may indicate…"
    /// </summary>
    Low = 1,

    /// <summary>
    /// Score &lt; 0.30. Speculative — flagged only for transparency.
    /// Language: "Lucid has observed a possible pattern, but cannot draw conclusions."
    /// Never surfaced as a recommendation.
    /// </summary>
    Speculative = 0,
}

public static class ConfidenceLevelExtensions
{
    /// <summary>Maps a continuous score (0–1) to the appropriate <see cref="ConfidenceLevel"/>.</summary>
    public static ConfidenceLevel FromScore(double score) => score switch
    {
        >= 0.90 => ConfidenceLevel.Certain,
        >= 0.70 => ConfidenceLevel.High,
        >= 0.50 => ConfidenceLevel.Medium,
        >= 0.30 => ConfidenceLevel.Low,
        _       => ConfidenceLevel.Speculative,
    };

    /// <summary>Returns the UI display label for this confidence level.</summary>
    public static string ToLabel(this ConfidenceLevel level) => level switch
    {
        ConfidenceLevel.Certain     => "High confidence",
        ConfidenceLevel.High        => "Strong signal",
        ConfidenceLevel.Medium      => "Moderate confidence",
        ConfidenceLevel.Low         => "Weak signal",
        ConfidenceLevel.Speculative => "Speculative",
        _                           => "Unknown",
    };

    /// <summary>Returns the UI hex accent color for this confidence level.</summary>
    public static string ToColor(this ConfidenceLevel level) => level switch
    {
        ConfidenceLevel.Certain     => "#34D399",  // green
        ConfidenceLevel.High        => "#60A5FA",  // blue
        ConfidenceLevel.Medium      => "#F59E0B",  // amber
        ConfidenceLevel.Low         => "#94A3B8",  // muted
        ConfidenceLevel.Speculative => "#6B7280",  // dim
        _                           => "#6B7280",
    };
}
