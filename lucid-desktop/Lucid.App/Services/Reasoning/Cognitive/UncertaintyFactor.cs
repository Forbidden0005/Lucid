namespace Lucid.Services.Reasoning.Cognitive;

/// <summary>
/// Identifies a named source of uncertainty that reduces confidence in an inference.
///
/// Uncertainty factors are collected during reasoning and surfaced in the UI
/// so users understand WHY a conclusion has limited confidence. Every factor
/// has a corresponding penalty applied by <see cref="ConfidenceScore.WithPenalty"/>.
///
/// This is foundational to Lucid's explainability guarantee:
/// speculation is never hidden, uncertainty is always named.
/// </summary>
public enum UncertaintyFactor
{
    /// <summary>Not enough data points to establish a pattern reliably.</summary>
    InsufficientDataPoints,

    /// <summary>The observation window is too short to draw trend conclusions.</summary>
    ShortObservationWindow,

    /// <summary>Evidence sources disagree — conflicting signals present.</summary>
    ConflictingSignals,

    /// <summary>The active workload context is ambiguous or unclassified.</summary>
    AmbiguousWorkloadContext,

    /// <summary>The primary evidence source has known reliability limitations.</summary>
    LimitedSourceReliability,

    /// <summary>The pattern is newly observed — no historical baseline for comparison.</summary>
    NoHistoricalBaseline,

    /// <summary>Attribution to a specific process is uncertain (multiple candidates).</summary>
    AmbiguousProcessAttribution,

    /// <summary>The observed metric is within normal range but approaching a threshold.</summary>
    BelowThresholdProximity,

    /// <summary>A recent user action may be responsible — causal attribution is unclear.</summary>
    RecentUserActionConfounder,

    /// <summary>The signal was only observed once — could be transient noise.</summary>
    SingleObservation,

    /// <summary>The inference depends on a forecast that carries its own uncertainty.</summary>
    ForecastDependency,
}

/// <summary>
/// A concrete uncertainty contribution from one factor, with an explanatory label
/// and the confidence penalty it applies.
/// </summary>
/// <param name="Factor">The source of uncertainty.</param>
/// <param name="Penalty">
/// Fractional confidence penalty (0–1). Applied multiplicatively by
/// <see cref="ConfidenceScore.WithPenalty"/>. Keep penalties conservative.
/// </param>
/// <param name="Detail">Optional human-readable detail for the Diagnostics UI.</param>
public sealed record UncertaintyContribution(
    UncertaintyFactor Factor,
    double            Penalty,
    string?           Detail = null)
{
    /// <summary>UI display label for this uncertainty factor.</summary>
    public string Label => Factor switch
    {
        UncertaintyFactor.InsufficientDataPoints     => "Not enough data",
        UncertaintyFactor.ShortObservationWindow     => "Short observation window",
        UncertaintyFactor.ConflictingSignals         => "Conflicting signals",
        UncertaintyFactor.AmbiguousWorkloadContext   => "Unclear workload context",
        UncertaintyFactor.LimitedSourceReliability   => "Limited source reliability",
        UncertaintyFactor.NoHistoricalBaseline       => "No historical baseline",
        UncertaintyFactor.AmbiguousProcessAttribution => "Unclear process attribution",
        UncertaintyFactor.BelowThresholdProximity    => "Below threshold — proximity only",
        UncertaintyFactor.RecentUserActionConfounder => "Recent user action may be responsible",
        UncertaintyFactor.SingleObservation          => "Single observation",
        UncertaintyFactor.ForecastDependency         => "Based on forecast projection",
        _                                             => "Uncertainty factor",
    };
}
