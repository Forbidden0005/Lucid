namespace Lucid.Services.Reasoning.Cognitive;

/// <summary>
/// Computed confidence score for an operational inference or recommendation.
///
/// Confidence is built compositionally:
///   1. Start with a base score derived from the primary evidence strength.
///   2. Apply a boost for each corroborating evidence piece (diminishing returns).
///   3. Apply multiplicative penalties for each <see cref="UncertaintyContribution"/>.
///   4. Clamp to [0, 1].
///
/// The full breakdown is preserved so Lucid can explain exactly why a confidence
/// level was assigned — a core explainability guarantee.
///
/// Immutable — create new instances rather than modifying existing ones.
/// </summary>
public sealed record ConfidenceScore
{
    /// <summary>Final computed confidence value in [0, 1].</summary>
    public double Value { get; init; }

    /// <summary>Discretized confidence level derived from <see cref="Value"/>.</summary>
    public ConfidenceLevel Level => ConfidenceLevelExtensions.FromScore(Value);

    /// <summary>The base score before corroboration and penalties were applied.</summary>
    public double BaseScore { get; init; }

    /// <summary>Total boost applied from corroborating evidence (+0–0.30 range).</summary>
    public double CorroborationBoost { get; init; }

    /// <summary>Active uncertainty factors that reduced the final score.</summary>
    public IReadOnlyList<UncertaintyContribution> UncertaintyFactors { get; init; } = [];

    /// <summary>Human-readable explanation of how this score was computed.</summary>
    public string Rationale { get; init; } = string.Empty;

    // ── Factory methods ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a score from a primary evidence strength, an optional number of
    /// corroborating signals, and any uncertainty penalties.
    /// </summary>
    public static ConfidenceScore Compute(
        EvidenceStrength                         primaryStrength,
        int                                      corroborationCount = 0,
        IEnumerable<UncertaintyContribution>?    uncertainties      = null)
    {
        var base_score   = primaryStrength.ToWeight();
        var boost        = ComputeBoost(corroborationCount);
        var pre_penalty  = Math.Min(1.0, base_score + boost);

        var factors      = uncertainties?.ToList() ?? [];
        var final        = ApplyPenalties(pre_penalty, factors);

        return new ConfidenceScore
        {
            Value              = Math.Clamp(final, 0.0, 1.0),
            BaseScore          = base_score,
            CorroborationBoost = boost,
            UncertaintyFactors = factors.AsReadOnly(),
            Rationale          = BuildRationale(primaryStrength, corroborationCount, factors, final),
        };
    }

    /// <summary>Creates a fixed-value score with an explicit rationale string.</summary>
    public static ConfidenceScore Fixed(double value, string rationale) =>
        new()
        {
            Value     = Math.Clamp(value, 0.0, 1.0),
            BaseScore = value,
            Rationale = rationale,
        };

    /// <summary>Returns a zero-confidence score indicating no meaningful evidence.</summary>
    public static ConfidenceScore None => Fixed(0.0, "No evidence available.");

    // ── Combination operators ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the minimum confidence score — use when multiple independent
    /// conditions must ALL hold for an inference to be valid.
    /// </summary>
    public static ConfidenceScore Min(ConfidenceScore a, ConfidenceScore b) =>
        a.Value <= b.Value ? a : b;

    /// <summary>
    /// Returns the weighted average of two scores.
    /// </summary>
    public static ConfidenceScore Average(ConfidenceScore a, ConfidenceScore b) =>
        Fixed((a.Value + b.Value) / 2.0, $"Average of {a.Value:P0} and {b.Value:P0}");

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static double ComputeBoost(int count)
    {
        // Diminishing returns: first corroboration gives the largest boost.
        // +0.10 / +0.08 / +0.05 / +0.04 / +0.03 for 1st–5th+
        return count switch
        {
            0 => 0.00,
            1 => 0.10,
            2 => 0.18,
            3 => 0.23,
            4 => 0.27,
            _ => 0.30,
        };
    }

    private static double ApplyPenalties(
        double                         score,
        IReadOnlyList<UncertaintyContribution> factors)
    {
        foreach (var f in factors)
            score *= (1.0 - f.Penalty);
        return score;
    }

    private static string BuildRationale(
        EvidenceStrength primaryStrength,
        int              corroborationCount,
        List<UncertaintyContribution> factors,
        double           final)
    {
        var parts = new List<string>
        {
            $"Primary evidence: {primaryStrength} ({primaryStrength.ToWeight():P0})",
        };
        if (corroborationCount > 0)
            parts.Add($"{corroborationCount} corroborating signal(s)");
        foreach (var f in factors)
            parts.Add($"Reduced by {f.Label} (−{f.Penalty:P0})");
        parts.Add($"Final: {final:P0}");
        return string.Join(" → ", parts);
    }

    /// <summary>Returns the UI display label for this score's level.</summary>
    public string Label => Level.ToLabel();

    /// <summary>Returns the UI accent color for this score's level.</summary>
    public string Color => Level.ToColor();

    /// <summary>Returns the score as a percentage string, e.g. "72%".</summary>
    public string Percentage => $"{Value:P0}";
}
