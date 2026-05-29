using Lucid.Services.Reasoning.Cognitive;

namespace Lucid.Services.Reasoning.Memory;

/// <summary>
/// A persisted record of an <see cref="OperationalInference"/> and whether
/// it proved to be accurate over time.
///
/// The reasoning memory layer allows the cognitive engine to:
///   - Detect recurring inference patterns ("startup congestion every Monday")
///   - Track whether high-confidence inferences were confirmed or contradicted
///   - Inform confidence adjustments for similar future patterns
///   - Build a lightweight audit trail for the Diagnostics page
///
/// Privacy guarantee: entries never store user-identifiable data.
/// Only operational signals (inference type, domain, confidence) are retained.
/// </summary>
public sealed record ReasoningMemoryEntry
{
    /// <summary>Unique entry identifier.</summary>
    public Guid EntryId { get; init; } = Guid.NewGuid();

    /// <summary>UTC time this entry was recorded.</summary>
    public DateTime RecordedAt { get; init; } = DateTime.UtcNow;

    // ── Inference summary ─────────────────────────────────────────────────────

    /// <summary>ID of the inference that generated this memory entry.</summary>
    public Guid InferenceId { get; init; }

    /// <summary>Domain(s) the inference covered (e.g. "Performance", "Thermal").</summary>
    public IReadOnlyList<string> Domains { get; init; } = [];

    /// <summary>Priority of the inference at the time it was produced.</summary>
    public InferencePriority Priority { get; init; }

    /// <summary>Confidence level at time of inference.</summary>
    public ConfidenceLevel ConfidenceAtInference { get; init; }

    /// <summary>Workload context label at time of inference.</summary>
    public string? WorkloadContext { get; init; }

    // ── Outcome tracking ──────────────────────────────────────────────────────

    /// <summary>
    /// Whether the inference was validated by a subsequent system state observation.
    /// Null until the outcome observation window expires.
    /// </summary>
    public bool? WasValidated { get; init; }

    /// <summary>
    /// Whether the user acted on a recommendation linked to this inference.
    /// Used to assess whether the inference was operationally actionable.
    /// </summary>
    public bool? UserActed { get; init; }

    /// <summary>UTC time the outcome was recorded. Null if not yet observed.</summary>
    public DateTime? OutcomeObservedAt { get; init; }
}
