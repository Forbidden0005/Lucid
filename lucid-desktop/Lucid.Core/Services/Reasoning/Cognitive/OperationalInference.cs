namespace Lucid.Services.Reasoning.Cognitive;

/// <summary>
/// A single operational inference produced by <see cref="ICognitiveReasoningEngine"/>.
///
/// An inference is a HIGHER-LEVEL INTERPRETATION than a raw <see cref="Lucid.Services.Intelligence.SystemInsight"/>.
/// Where an insight detects a condition ("CPU is elevated"),
/// an inference contextualizes it ("startup congestion is causing transient resource pressure").
///
/// Every inference:
///   - Has a confidence score derived from contributing evidence
///   - Traces back to specific evidence pieces (fully inspectable)
///   - Is labeled with its priority relative to current context
///   - Carries optional recommended action IDs
///   - Never claims certainty beyond what evidence supports
///
/// Immutable record — share across threads without synchronization.
/// </summary>
public sealed record OperationalInference
{
    /// <summary>Unique identifier for this inference instance.</summary>
    public Guid InferenceId { get; init; } = Guid.NewGuid();

    /// <summary>UTC time this inference was produced.</summary>
    public DateTime ProducedAt { get; init; } = DateTime.UtcNow;

    // ── Content ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Short headline (≤ 80 chars) stating what the engine concluded.
    /// Written in plain language, never as a severity warning.
    /// Example: "Startup congestion is causing transient resource pressure."
    /// </summary>
    public string Headline { get; init; } = string.Empty;

    /// <summary>
    /// 1–3 sentence explanation of why this conclusion was reached.
    /// References the contributing evidence without repeating metric numbers verbatim.
    /// </summary>
    public string Explanation { get; init; } = string.Empty;

    /// <summary>
    /// What is likely to happen if this condition continues unchanged.
    /// Written probabilistically — never as a certainty statement.
    /// </summary>
    public string? Trajectory { get; init; }

    // ── Classification ────────────────────────────────────────────────────────

    /// <summary>Priority of this inference in the current operational context.</summary>
    public InferencePriority Priority { get; init; } = InferencePriority.Advisory;

    /// <summary>Computed confidence for this inference.</summary>
    public ConfidenceScore Confidence { get; init; } = ConfidenceScore.None;

    /// <summary>
    /// Domain categories this inference spans.
    /// Examples: "Performance", "Startup", "Thermal", "Storage", "Security".
    /// </summary>
    public IReadOnlyList<string> Domains { get; init; } = [];

    // ── Evidence ──────────────────────────────────────────────────────────────

    /// <summary>
    /// All evidence pieces that contributed to this inference, in descending strength order.
    /// Must be non-empty — inferences without evidence are invalid.
    /// </summary>
    public IReadOnlyList<ReasoningEvidence> Evidence { get; init; } = [];

    /// <summary>
    /// Optional correlation ID linking this inference to a broader operational chain.
    /// Matches <see cref="Lucid.Services.Infrastructure.Events.OperationalEvent.CorrelationId"/>.
    /// </summary>
    public string? CorrelationId { get; init; }

    // ── Action links ──────────────────────────────────────────────────────────

    /// <summary>
    /// IDs of <see cref="Lucid.Services.Intelligence.SystemAction"/> instances that
    /// directly address this inference's root cause.
    /// Empty if no actionable remediation is available.
    /// </summary>
    public IReadOnlyList<string> RecommendedActionIds { get; init; } = [];

    /// <summary>
    /// IDs of the source <see cref="Lucid.Services.Intelligence.SystemInsight"/> instances
    /// that contributed to this inference, for drill-down navigation.
    /// </summary>
    public IReadOnlyList<string> SourceInsightIds { get; init; } = [];

    // ── Context ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The workload context label at the time this inference was produced.
    /// Comes from <see cref="Context.ContextProfile.WorkloadLabel"/>.
    /// Example: "Gaming", "Compiling", "Idle", "Video streaming".
    /// </summary>
    public string? WorkloadContextAtInference { get; init; }

    /// <summary>
    /// True if this inference is suppressed by the current context (e.g., thermal
    /// spike during expected high-load workload). Suppressed inferences are logged
    /// but not surfaced in the UI recommendation list.
    /// </summary>
    public bool IsSuppressed { get; init; }

    /// <summary>Reason for suppression, if <see cref="IsSuppressed"/> is true.</summary>
    public string? SuppressionReason { get; init; }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>True if this inference has actionable recommended actions.</summary>
    public bool IsActionable => RecommendedActionIds.Count > 0 && !IsSuppressed;

    /// <summary>True if confidence is at least Medium and inference is not suppressed.</summary>
    public bool IsReliable =>
        Confidence.Level >= ConfidenceLevel.Medium && !IsSuppressed;

    /// <summary>
    /// Returns a suppressed copy of this inference with the given reason.
    /// </summary>
    public OperationalInference AsSuppressed(string reason) =>
        this with { IsSuppressed = true, SuppressionReason = reason };
}
