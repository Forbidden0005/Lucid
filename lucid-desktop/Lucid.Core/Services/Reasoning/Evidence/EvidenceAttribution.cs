namespace Lucid.Services.Reasoning.Evidence;

/// <summary>
/// Immutable attribution record describing one subsystem's contribution
/// to a cognitive inference.
///
/// Used to produce a full provenance chain for any
/// <see cref="Lucid.Services.Reasoning.Cognitive.OperationalInference"/>:
/// "this conclusion was reached because telemetry showed X, the insight
/// engine flagged Y, and the anomaly detector observed Z."
///
/// ConfidenceWeight (0–1) represents how much this attribution influenced
/// the overall inference confidence relative to other attributions in the
/// same chain. The sum of all weights in a chain should equal 1.0.
/// </summary>
public sealed record EvidenceAttribution
{
    /// <summary>The subsystem that produced this evidence.</summary>
    public required EvidenceSourceType SourceType { get; init; }

    /// <summary>Human-readable name of the subsystem (e.g. "SystemInsightEngine", "AnomalyDetectionService").</summary>
    public required string SubsystemName { get; init; }

    /// <summary>Plain-language description of the evidence observed.</summary>
    public required string Description { get; init; }

    /// <summary>
    /// How much this attribution contributed to the overall confidence (0–1).
    /// Weights across all attributions in one inference should sum to 1.0.
    /// </summary>
    public required double ConfidenceWeight { get; init; }

    /// <summary>UTC time this evidence was observed or computed.</summary>
    public required DateTime ObservedAtUtc { get; init; }

    /// <summary>Optional stable identifier linking to the source object (insight ID, action ID, etc.).</summary>
    public string? SourceId { get; init; }

    /// <summary>Optional raw metric value for telemetry-type attributions (e.g. "87.3").</summary>
    public string? RawValue { get; init; }

    // ── Factory helpers ─────────────────────────────────────────────────────────

    public static EvidenceAttribution FromTelemetry(
        string description, double weight, string? rawValue = null) =>
        new()
        {
            SourceType       = EvidenceSourceType.Telemetry,
            SubsystemName    = "WindowsTelemetryService",
            Description      = description,
            ConfidenceWeight = weight,
            ObservedAtUtc    = DateTime.UtcNow,
            RawValue         = rawValue,
        };

    public static EvidenceAttribution FromInsight(
        string insightId, string description, double weight) =>
        new()
        {
            SourceType       = EvidenceSourceType.InsightEngine,
            SubsystemName    = "SystemInsightEngine",
            Description      = description,
            ConfidenceWeight = weight,
            ObservedAtUtc    = DateTime.UtcNow,
            SourceId         = insightId,
        };

    public static EvidenceAttribution FromGovernance(
        string description, double weight) =>
        new()
        {
            SourceType       = EvidenceSourceType.GovernanceState,
            SubsystemName    = "GovernanceDiagnosticsService",
            Description      = description,
            ConfidenceWeight = weight,
            ObservedAtUtc    = DateTime.UtcNow,
        };

    public static EvidenceAttribution FromMemory(
        string description, double weight) =>
        new()
        {
            SourceType       = EvidenceSourceType.ReasoningMemory,
            SubsystemName    = "ReasoningMemoryService",
            Description      = description,
            ConfidenceWeight = weight,
            ObservedAtUtc    = DateTime.UtcNow,
        };
}
