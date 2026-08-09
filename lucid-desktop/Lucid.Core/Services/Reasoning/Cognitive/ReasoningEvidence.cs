using Lucid.Services.Reasoning;

namespace Lucid.Services.Reasoning.Cognitive;

/// <summary>
/// A reference to a single piece of evidence that contributed to an
/// <see cref="OperationalInference"/>.
///
/// Evidence references use the existing <see cref="EvidenceNodeType"/> taxonomy
/// and link back to source objects (insights, actions, processes) by ID so the
/// Diagnostics and Insight Detail pages can render full evidence chains.
///
/// Immutable — created once per reasoning cycle, never mutated.
/// </summary>
/// <param name="NodeType">Category of evidence this reference points to.</param>
/// <param name="SourceId">
/// Optional ID of the source object (insight ID, action ID, process PID, etc.).
/// </param>
/// <param name="Description">
/// Human-readable summary of what this evidence shows.
/// Must not contain user-identifiable data (file paths, names, queries).
/// </param>
/// <param name="Strength">How strong this particular piece of evidence is.</param>
/// <param name="ObservedAt">When this evidence was recorded.</param>
public sealed record ReasoningEvidence(
    EvidenceNodeType NodeType,
    string?          SourceId,
    string           Description,
    EvidenceStrength Strength,
    DateTime         ObservedAt)
{
    /// <summary>
    /// Creates a telemetry evidence reference from a metric name and observation summary.
    /// </summary>
    public static ReasoningEvidence Telemetry(string metricDescription, EvidenceStrength strength) =>
        new(EvidenceNodeType.Telemetry, null, metricDescription, strength, DateTime.UtcNow);

    /// <summary>Creates an insight evidence reference.</summary>
    public static ReasoningEvidence Insight(string insightId, string summary, EvidenceStrength strength) =>
        new(EvidenceNodeType.Insight, insightId, summary, strength, DateTime.UtcNow);

    /// <summary>Creates an anomaly evidence reference.</summary>
    public static ReasoningEvidence Anomaly(string summary, EvidenceStrength strength) =>
        new(EvidenceNodeType.Anomaly, null, summary, strength, DateTime.UtcNow);

    /// <summary>Creates a forecast evidence reference.</summary>
    public static ReasoningEvidence Forecast(string summary, EvidenceStrength strength) =>
        new(EvidenceNodeType.Forecast, null, summary, strength, DateTime.UtcNow);

    /// <summary>Creates a process attribution evidence reference.</summary>
    public static ReasoningEvidence Process(string processName, string summary, EvidenceStrength strength) =>
        new(EvidenceNodeType.Process, processName, summary, strength, DateTime.UtcNow);
}
