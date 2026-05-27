namespace Lucid.Services.Narrative;

/// <summary>
/// The overall system health tier, derived from active warning count.
/// Used to drive headline phrasing, icon, and accent colour in the UI.
/// </summary>
public enum SystemHealthState
{
    /// <summary>No active findings. SystemRunningWellRule is the only active insight.</summary>
    Healthy    = 0,

    /// <summary>One or more Recommendations active, but no Warnings.</summary>
    Monitoring = 1,

    /// <summary>One or two active Warnings.</summary>
    Degraded   = 2,

    /// <summary>Three or more active Warnings.</summary>
    Critical   = 3,
}

/// <summary>
/// Immutable output of <see cref="IOperationalNarrativeEngine"/> for one evaluation cycle.
///
/// A narrative is composed of a short headline and up to five thematic paragraphs:
///   1. Status + current metrics    — always present
///   2. Active issues synthesis     — present when issues exist
///   3. Process attribution         — present when processes are identified
///   4. Forecast outlook            — present when forecast rules have fired
///   5. Baseline / machine context  — present when anomaly rules have fired
///
/// All text is human-readable, confidence-aware prose produced entirely by
/// deterministic template rules — no LLM or cloud service is involved.
/// </summary>
public sealed record OperationalNarrative(
    /// <summary>Derived health tier driving visual presentation.</summary>
    SystemHealthState HealthState,

    /// <summary>
    /// Short verdict sentence (≤12 words) for the top of the narrative panel.
    /// Examples: "Your system is running well.", "Three issues need attention."
    /// </summary>
    string Headline,

    /// <summary>
    /// Ordered paragraphs of narrative prose.
    /// At least one element is always present. At most five.
    /// </summary>
    string[] Paragraphs,

    /// <summary>UTC time when this narrative was generated.</summary>
    DateTimeOffset GeneratedAt,

    /// <summary>Number of active Warning-severity findings (excludes Recommendations).</summary>
    int ActiveWarnings,

    /// <summary>
    /// Total number of actionable findings (excludes the "system.healthy" informational).
    /// </summary>
    int ActiveFindings)
{
    /// <summary>Convenience: true when no actionable findings are active.</summary>
    public bool IsHealthy => HealthState == SystemHealthState.Healthy;

    /// <summary>The first paragraph — always the status + metrics overview.</summary>
    public string StatusParagraph => Paragraphs.Length > 0 ? Paragraphs[0] : string.Empty;
}
