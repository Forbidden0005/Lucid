namespace Lucid.Services.Reasoning.Cognitive;

/// <summary>
/// Qualitative strength of a single piece of evidence contributing to an inference.
///
/// Strength is assessed by the reasoning engine based on the evidence source type,
/// observation duration, and agreement with other signals. It contributes to
/// <see cref="ConfidenceScore"/> alongside quantity and source reliability.
/// </summary>
public enum EvidenceStrength
{
    /// <summary>
    /// Strong: observed consistently over multiple samples with high signal-to-noise ratio.
    /// Examples: 5-minute sustained CPU above threshold, confirmed baseline deviation.
    /// </summary>
    Strong = 3,

    /// <summary>
    /// Moderate: observed in multiple samples but with some variance or short duration.
    /// Examples: 2-minute elevation, partially corroborated anomaly.
    /// </summary>
    Moderate = 2,

    /// <summary>
    /// Weak: single observation, short duration, or noisy signal.
    /// Examples: one-sample spike, marginally above threshold.
    /// </summary>
    Weak = 1,

    /// <summary>
    /// Circumstantial: inferred indirectly rather than directly observed.
    /// Examples: process attribution derived from timing correlation only.
    /// </summary>
    Circumstantial = 0,
}

public static class EvidenceStrengthExtensions
{
    /// <summary>Returns the base confidence weight for this strength tier.</summary>
    public static double ToWeight(this EvidenceStrength strength) => strength switch
    {
        EvidenceStrength.Strong        => 0.85,
        EvidenceStrength.Moderate      => 0.60,
        EvidenceStrength.Weak          => 0.35,
        EvidenceStrength.Circumstantial => 0.15,
        _                              => 0.30,
    };
}
