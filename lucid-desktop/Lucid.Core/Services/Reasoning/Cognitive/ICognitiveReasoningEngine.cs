namespace Lucid.Services.Reasoning.Cognitive;

/// <summary>
/// The central reasoning orchestrator for the Lucid platform.
///
/// The cognitive reasoning engine synthesizes signals from all subsystems into
/// a set of <see cref="OperationalInference"/> objects — higher-level contextual
/// interpretations that go beyond individual insight detections.
///
/// Where <see cref="Lucid.Services.Intelligence.ISystemInsightEngine"/> detects
/// conditions ("CPU is elevated"), the cognitive engine interprets them:
/// "Startup congestion is causing transient resource pressure — expect normalization
/// within 5 minutes."
///
/// Design principles:
///   - Deterministic: same inputs always produce the same outputs
///   - Transparent: every inference traces to specific evidence
///   - Confidence-aware: uncertainty is always modeled and surfaced
///   - Context-sensitive: context adjusts interpretation, never hides facts
///   - Non-autonomous: produces conclusions only — never acts on its own
///
/// Thread-safe: all methods are safe for concurrent callers.
/// </summary>
public interface ICognitiveReasoningEngine
{
    /// <summary>
    /// The most recent set of operational inferences, ordered by priority descending.
    /// Updated after each <see cref="AnalyzeAsync"/> call.
    /// Empty before the first analysis cycle.
    /// </summary>
    IReadOnlyList<OperationalInference> CurrentInferences { get; }

    /// <summary>
    /// The most recent reasoning context used to produce <see cref="CurrentInferences"/>.
    /// Null before the first analysis cycle.
    /// </summary>
    ReasoningContext? LastContext { get; }

    /// <summary>
    /// UTC time of the most recent analysis cycle. Null before first cycle.
    /// </summary>
    DateTime? LastAnalysisAt { get; }

    /// <summary>
    /// Runs a full reasoning cycle:
    ///   1. Assembles a <see cref="ReasoningContext"/> from all active signal sources.
    ///   2. Evaluates inference rules against the context.
    ///   3. Assigns confidence scores based on evidence strength and uncertainty.
    ///   4. Applies context suppression for contextually appropriate signals.
    ///   5. Records inferences in the reasoning memory layer.
    ///   6. Publishes a <see cref="Lucid.Services.Infrastructure.Events.CognitiveInferenceCycleCompletedEvent"/>
    ///      to the event bus.
    ///
    /// Designed to complete in &lt; 50 ms on typical hardware.
    /// Safe to call from the telemetry loop.
    /// </summary>
    Task<IReadOnlyList<OperationalInference>> AnalyzeAsync(
        CancellationToken cancellationToken = default);
}
