using Lucid.Services.Intelligence.Arbitration;
using Lucid.Services.Interaction.Language;
using Lucid.Services.Reasoning.Cognitive;

namespace Lucid.Services.Interaction.Presentation;

/// <summary>
/// Converts raw <see cref="OperationalInference"/> objects into fully-formatted
/// <see cref="OperationalInsightPresentation"/> records ready for UI binding.
///
/// The presenter bridges the cognitive reasoning output and the presentation layer:
///   - Applies the language policy
///   - Formats confidence and severity labels
///   - Derives urgency classification
///   - Produces compact and standard text variants
///
/// Thread-safe: stateless and pure.
/// </summary>
public static class OperationalInsightPresenter
{
    /// <summary>
    /// Converts a single <see cref="OperationalInference"/> to a presentation record.
    /// </summary>
    public static OperationalInsightPresentation Present(OperationalInference inference)
    {
        var severity = ClassifySeverity(inference);

        return new OperationalInsightPresentation
        {
            Title              = CalmCommunicationFormatter.FormatCompact(inference.Headline),
            Summary            = CalmCommunicationFormatter.FormatStandard(inference.Explanation),
            DetailText         = BuildDetail(inference),
            TrajectoryText     = FormatTrajectory(inference.Trajectory),
            ConfidenceLabel    = inference.Confidence.Label,
            ConfidenceValue    = inference.Confidence.Value,
            SeverityLabel      = SeverityNarrativeEngine.GetLabel(severity),
            SeverityColorToken = SeverityNarrativeEngine.GetColorToken(severity),
            Domains            = inference.Domains,
            IsUrgent           = inference.Priority >= InferencePriority.Elevated,
            IsSuppressed       = inference.IsSuppressed,
            SuppressionReason  = inference.SuppressionReason,
            RelatedActionIds   = inference.RecommendedActionIds,
            SourceInferenceId  = inference.InferenceId,
        };
    }

    /// <summary>
    /// Converts a list of inferences to presentation records, active items first.
    /// </summary>
    public static IReadOnlyList<OperationalInsightPresentation> PresentAll(
        IEnumerable<OperationalInference> inferences) =>
        inferences
            .OrderBy(i => i.IsSuppressed)
            .ThenByDescending(i => i.Priority)
            .Select(Present)
            .ToList()
            .AsReadOnly();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? BuildDetail(OperationalInference inference)
    {
        var parts = new List<string>(3);

        if (!string.IsNullOrWhiteSpace(inference.Explanation))
            parts.Add(inference.Explanation);

        // Append the confidence explanation.
        var confidenceNote = ConfidenceToneAdjuster.GetHedgeClause(inference.Confidence.Level);
        if (confidenceNote is not null)
            parts.Add(confidenceNote);

        // Append workload context if it explains the situation.
        if (!string.IsNullOrWhiteSpace(inference.WorkloadContextAtInference))
            parts.Add($"Context at inference time: {inference.WorkloadContextAtInference}.");

        return parts.Count > 0
            ? CalmCommunicationFormatter.FormatDetail(string.Join(" ", parts))
            : null;
    }

    private static string? FormatTrajectory(string? trajectory)
    {
        if (string.IsNullOrWhiteSpace(trajectory)) return null;
        return CalmCommunicationFormatter.FormatStandard(trajectory);
    }

    /// <summary>
    /// Maps inference priority to the nearest RecommendationSeverity tier
    /// for color coding and urgency label selection.
    /// </summary>
    private static RecommendationSeverity ClassifySeverity(OperationalInference inference) =>
        inference.Priority switch
        {
            InferencePriority.Elevated      => RecommendationSeverity.Stability,
            InferencePriority.Advisory      => RecommendationSeverity.Performance,
            InferencePriority.Informational => RecommendationSeverity.Maintenance,
            InferencePriority.Background    => RecommendationSeverity.Cosmetic,
            _                               => RecommendationSeverity.Maintenance,
        };
}
