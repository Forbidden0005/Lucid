using Lucid.Services.Diagnostics.Reasoning;
using Lucid.Services.Intelligence;
using Lucid.Services.Interaction.Language;
using Lucid.Services.Reasoning.Cognitive;

namespace Lucid.Services.Interaction.Explainability;

/// <summary>
/// Composites inference evidence, confidence breakdown, and arbitration context
/// into a single <see cref="InferenceExplanation"/> record for the UI.
///
/// The ExplainabilityRenderer is the primary entry point for the Explainability
/// UX panel — it composes all sub-builders into one coherent explanation
/// that the ViewModel can bind directly.
///
/// Used by:
///   - DiagnosticsPageViewModel — full inference trace with evidence
///   - InsightDetailViewModel   — per-insight explanation
///   - ReasoningDiagnosticsPage — arbitration audit trail
///
/// Thread-safe: stateless and pure — all inputs are immutable snapshots.
/// </summary>
public sealed class ExplainabilityRenderer
{
    private readonly IRecommendationExplanationService? _recommendationExplanation;

    public ExplainabilityRenderer(
        IRecommendationExplanationService? recommendationExplanation = null)
    {
        _recommendationExplanation = recommendationExplanation;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders a complete explanation for an operational inference.
    /// </summary>
    public InferenceExplanation Render(OperationalInference inference)
    {
        return new InferenceExplanation
        {
            InferenceId         = inference.InferenceId,
            Headline            = CalmCommunicationFormatter.FormatCompact(inference.Headline),
            FullExplanation     = CalmCommunicationFormatter.FormatDetail(inference.Explanation),
            TrajectoryText      = inference.Trajectory is not null
                                      ? CalmCommunicationFormatter.FormatStandard(inference.Trajectory)
                                      : null,
            ConfidenceSummary   = ConfidenceNarrativeBuilder.BuildStandard(inference.Confidence),
            ConfidenceDetail    = ConfidenceNarrativeBuilder.BuildFull(inference.Confidence),
            EvidenceSummary     = EvidenceNarrativeBuilder.BuildCompact(inference.Evidence),
            EvidenceDetail      = EvidenceNarrativeBuilder.BuildFull(inference.Evidence),
            StructuredEvidence  = EvidenceNarrativeBuilder.BuildStructured(inference.Evidence),
            IsSuppressed        = inference.IsSuppressed,
            SuppressionReason   = inference.SuppressionReason is not null
                                      ? CalmCommunicationFormatter.FormatStandard(
                                            inference.SuppressionReason)
                                      : null,
            WorkloadContext     = inference.WorkloadContextAtInference,
            ProducedAt          = inference.ProducedAt,
        };
    }

    /// <summary>
    /// Renders an explanation for an arbitration decision.
    /// </summary>
    public ArbitrationExplanation RenderArbitration(ArbitrationDecisionRecord decision) =>
        new()
        {
            ActionId           = decision.ActionId,
            SourceInsightId    = decision.SourceInsightId,
            WasSurfaced        = decision.Passed,
            CompactExplanation = ArbitrationNarrativeBuilder.BuildCompact(decision),
            FullExplanation    = ArbitrationNarrativeBuilder.BuildFull(decision),
            SuppressionSummary = ArbitrationNarrativeBuilder.BuildSuppressionSummary(decision),
            SeverityLabel      = SeverityNarrativeEngine.GetLabel(decision.Severity),
            OutcomeLabel       = decision.OutcomeLabel,
        };
}

// ── Output models ─────────────────────────────────────────────────────────────

/// <summary>
/// Fully-rendered explanation for one operational inference.
/// Ready for ViewModel binding — no further formatting required.
/// </summary>
public sealed record InferenceExplanation
{
    public Guid InferenceId { get; init; }
    public string Headline { get; init; } = string.Empty;
    public string FullExplanation { get; init; } = string.Empty;
    public string? TrajectoryText { get; init; }
    public string ConfidenceSummary { get; init; } = string.Empty;
    public string ConfidenceDetail { get; init; } = string.Empty;
    public string EvidenceSummary { get; init; } = string.Empty;
    public string EvidenceDetail { get; init; } = string.Empty;
    public IReadOnlyList<(string Source, string Strength)> StructuredEvidence { get; init; } = [];
    public bool IsSuppressed { get; init; }
    public string? SuppressionReason { get; init; }
    public string? WorkloadContext { get; init; }
    public DateTime ProducedAt { get; init; }
}

/// <summary>
/// Fully-rendered explanation for one arbitration decision.
/// </summary>
public sealed record ArbitrationExplanation
{
    public string ActionId { get; init; } = string.Empty;
    public string SourceInsightId { get; init; } = string.Empty;
    public bool WasSurfaced { get; init; }
    public string CompactExplanation { get; init; } = string.Empty;
    public string FullExplanation { get; init; } = string.Empty;
    public string? SuppressionSummary { get; init; }
    public string SeverityLabel { get; init; } = string.Empty;
    public string OutcomeLabel { get; init; } = string.Empty;
}
