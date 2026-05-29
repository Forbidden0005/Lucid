using Lucid.Services.Intelligence;
using Lucid.Services.Interaction.Language;
using Lucid.Services.Reasoning.Cognitive;

namespace Lucid.Services.Interaction.Presentation;

/// <summary>
/// Central service that synthesizes cognitive engine output and intelligence
/// findings into a <see cref="CognitivePresentationSnapshot"/> ready for
/// direct ViewModel consumption.
///
/// Bridges the cognitive reasoning layer and the UI layer:
///   - Reads from <see cref="ICognitiveReasoningEngine"/> for high-level inferences
///   - Reads from <see cref="ISystemInsightEngine"/> for rule-based findings
///   - Produces policy-compliant, fully-formatted presentation records
///
/// Synchronous and cheap — does not trigger new reasoning cycles.
/// Thread-safe: reads from immutable snapshots only, no mutable state.
/// </summary>
public sealed class CognitivePresentationService
{
    private readonly ICognitiveReasoningEngine _cognitiveEngine;
    private readonly ISystemInsightEngine      _insightEngine;

    public CognitivePresentationService(
        ICognitiveReasoningEngine cognitiveEngine,
        ISystemInsightEngine      insightEngine)
    {
        _cognitiveEngine = cognitiveEngine;
        _insightEngine   = insightEngine;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="CognitivePresentationSnapshot"/> from the latest
    /// cognitive engine output.
    ///
    /// This is a synchronous read — it does not trigger a new reasoning cycle.
    /// Call <c>ICognitiveReasoningEngine.AnalyzeAsync()</c> first to refresh the engine.
    /// </summary>
    public CognitivePresentationSnapshot BuildSnapshot()
    {
        var inferences = _cognitiveEngine.CurrentInferences;
        var active     = new List<OperationalInsightPresentation>();
        var suppressed = new List<OperationalInsightPresentation>();

        foreach (var inf in inferences)
        {
            var presented = OperationalInsightPresenter.Present(inf);
            if (inf.IsSuppressed)
                suppressed.Add(presented);
            else
                active.Add(presented);
        }

        // Priority sort: urgent first, then by confidence descending.
        active.Sort((a, b) =>
        {
            if (a.IsUrgent != b.IsUrgent)
                return b.IsUrgent.CompareTo(a.IsUrgent);
            return b.ConfidenceValue.CompareTo(a.ConfidenceValue);
        });

        return new CognitivePresentationSnapshot
        {
            AssembledAt        = DateTime.UtcNow,
            ActiveInsights     = active.AsReadOnly(),
            SuppressedInsights = suppressed.AsReadOnly(),
        };
    }

    /// <summary>
    /// Returns presentations for the current intelligence engine findings.
    /// These are lower-level than cognitive inferences — useful for the Insights page.
    /// </summary>
    public IReadOnlyList<OperationalInsightPresentation> GetInsightPresentations() =>
        _insightEngine.CurrentInsights
            .Select(PresentInsight)
            .OrderByDescending(p => p.IsUrgent)
            .ThenByDescending(p => p.ConfidenceValue)
            .ToList()
            .AsReadOnly();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static OperationalInsightPresentation PresentInsight(SystemInsight insight)
    {
        var severityToken = insight.Severity switch
        {
            InsightSeverity.Warning        => "Severity.Warning",
            InsightSeverity.Recommendation => "Severity.Advisory",
            InsightSeverity.Info           => "Severity.Info",
            _                              => "Severity.Info",
        };

        return new OperationalInsightPresentation
        {
            Title              = CalmCommunicationFormatter.FormatCompact(insight.Title),
            Summary            = CalmCommunicationFormatter.FormatStandard(insight.Detail),
            DetailText         = BuildInsightDetail(insight),
            SeverityLabel      = insight.Severity == InsightSeverity.Warning
                                     ? "Needs attention"
                                     : insight.Severity == InsightSeverity.Recommendation
                                         ? "Actionable improvement"
                                         : "Informational",
            SeverityColorToken = severityToken,
            ConfidenceLabel    = $"{insight.ConfidencePercent}% confidence",
            ConfidenceValue    = insight.ConfidencePercent / 100.0,
            IsUrgent           = insight.Severity == InsightSeverity.Warning,
            Domains            = [],    // SystemInsight does not expose domain list
            RelatedActionIds   = insight.RecommendedActions.Select(a => a.Id).ToList().AsReadOnly(),
        };
    }

    private static string? BuildInsightDetail(SystemInsight insight)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(insight.Detail))
            parts.Add(insight.Detail);
        if (!string.IsNullOrWhiteSpace(insight.ActionHint))
            parts.Add($"Suggested next step: {insight.ActionHint}");
        return parts.Count > 0
            ? CalmCommunicationFormatter.FormatDetail(string.Join(" ", parts))
            : null;
    }
}
