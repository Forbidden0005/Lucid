namespace Lucid.Services.Diagnostics.Learning;

/// <summary>
/// Immutable record of a single adaptive learning event.
///
/// A LearningTraceRecord is created whenever Lucid updates its understanding:
///   - A new pattern was recognized
///   - A baseline was adjusted
///   - A recommendation's effectiveness rating changed
///   - Confidence calibration was updated
///   - A drift alert was recorded
///
/// These records form the "learning audit trail" — everything Lucid has learned
/// is recorded here so users can always ask "what has Lucid adapted?"
///
/// Privacy guarantee: no user-identifiable data. Operational signals only.
/// </summary>
public sealed record LearningTraceRecord
{
    /// <summary>Unique identifier for this trace.</summary>
    public Guid TraceId { get; init; } = Guid.NewGuid();

    /// <summary>UTC time this learning event occurred.</summary>
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    /// <summary>Category of the learning event.</summary>
    public required LearningEventType EventType { get; init; }

    /// <summary>Human-readable description of what was learned.</summary>
    public required string Description { get; init; }

    /// <summary>Evidence summary that drove this learning update.</summary>
    public string? EvidenceSummary { get; init; }

    /// <summary>
    /// How confident Lucid is in this learning update (0–1).
    /// Low confidence = early/tentative adaptation.
    /// </summary>
    public double LearningConfidence { get; init; } = 0.5;

    /// <summary>
    /// Whether any governance constraint constrained or modified this learning event.
    /// </summary>
    public string? GovernanceNote { get; init; }

    /// <summary>Inference type or action ID this event pertains to (if applicable).</summary>
    public string? RelatedEntityId { get; init; }
}

/// <summary>Categories of adaptive learning events.</summary>
public enum LearningEventType
{
    /// <summary>A new recurring operational pattern was recognized.</summary>
    PatternRecognized,

    /// <summary>A baseline profile was updated with new observations.</summary>
    BaselineUpdated,

    /// <summary>A recommendation's effectiveness rating changed.</summary>
    EffectivenessUpdated,

    /// <summary>Confidence calibration was adjusted for an inference type.</summary>
    CalibrationAdjusted,

    /// <summary>A drift alert was recorded and analyzed.</summary>
    DriftRecorded,

    /// <summary>A learning event was suppressed by governance policy.</summary>
    GovernanceSuppressed,
}
