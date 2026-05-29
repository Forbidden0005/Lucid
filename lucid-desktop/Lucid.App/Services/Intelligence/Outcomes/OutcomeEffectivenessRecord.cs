namespace Lucid.Services.Intelligence.Outcomes;

/// <summary>
/// Immutable record linking a recommendation outcome to its effectiveness signal.
///
/// Where <see cref="Lucid.Services.Reasoning.Memory.RecommendationOutcomeTracker"/>
/// records raw outcome events (accepted/rejected/dismissed/expired), this record
/// combines those signals with the associated operational context to compute a
/// richer effectiveness score.
///
/// Used by <see cref="RecommendationEffectivenessAnalyzer"/> to assess
/// whether a recommendation type is genuinely improving the system.
/// </summary>
public sealed record OutcomeEffectivenessRecord
{
    /// <summary>The action ID this record applies to.</summary>
    public required string ActionId { get; init; }

    /// <summary>The insight or inference type that generated this recommendation.</summary>
    public string? SourceInsightId { get; init; }

    /// <summary>UTC time this record was created.</summary>
    public DateTime RecordedAt { get; init; } = DateTime.UtcNow;

    // ── Outcome signals ───────────────────────────────────────────────────────

    /// <summary>Number of times this recommendation was accepted by the user.</summary>
    public required int AcceptanceCount { get; init; }

    /// <summary>Number of times this recommendation was rejected/dismissed.</summary>
    public required int DismissalCount { get; init; }

    /// <summary>Number of times this recommendation expired without user interaction.</summary>
    public required int ExpiryCount { get; init; }

    /// <summary>
    /// True when the underlying condition improved after action was taken.
    /// Null when no condition change was observed (too early to assess).
    /// </summary>
    public bool? ConditionImproved { get; init; }

    // ── Computed ──────────────────────────────────────────────────────────────

    /// <summary>Total times this recommendation was shown.</summary>
    public int TotalShown => AcceptanceCount + DismissalCount + ExpiryCount;

    /// <summary>Acceptance rate in [0, 1]. 0.5 when no data.</summary>
    public double AcceptanceRate =>
        TotalShown == 0 ? 0.5 : (double)AcceptanceCount / TotalShown;

    /// <summary>
    /// Composite effectiveness score in [0, 1].
    /// Incorporates acceptance rate + condition improvement signal.
    /// </summary>
    public double EffectivenessScore
    {
        get
        {
            if (TotalShown == 0) return 0.5;

            double score = AcceptanceRate;

            // Condition improvement is a strong positive signal.
            if (ConditionImproved == true)  score = Math.Min(1.0, score + 0.25);
            if (ConditionImproved == false) score = Math.Max(0.0, score - 0.15);

            return score;
        }
    }

    /// <summary>True when there is enough data to make meaningful assessments.</summary>
    public bool HasSufficientData => TotalShown >= 5;

    /// <summary>True when this recommendation appears to be low-value (score &lt; 0.3).</summary>
    public bool IsLowValue => HasSufficientData && EffectivenessScore < 0.3;
}
