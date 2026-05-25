using Lucid.Services.Intelligence;

namespace Lucid.Services.Explain;

/// <summary>
/// Maps active intelligence findings to <see cref="CausalFactor"/> instances,
/// grouping related insights into a ranked causal chain.
///
/// Each non-trivial active insight (severity ≥ Recommendation, excluding
/// system.healthy) becomes one causal factor. Factors are ordered:
///   1. Warnings before recommendations
///   2. Higher confidence before lower confidence
///
/// The domain for each factor is inferred from the insight's ID prefix:
///   cpu.*      → Cpu
///   ram.*      → Memory
///   disk.*     → Disk
///   startup.*  → Startup
///   thermal.*  → Thermal
///   security.* → Security
///   process.*  → Process
///   forecast.* → handled separately by ExplanationComposer
///   (other)    → Unknown
///
/// Forecast insights are excluded here — they appear in the "What Happens Next?"
/// section instead of the "What's Causing This?" section.
/// </summary>
internal static class OperationalReasoningGraph
{
    internal static IReadOnlyList<CausalFactor> Build(IReadOnlyList<SystemInsight> insights)
    {
        var factors = new List<CausalFactor>();

        foreach (var insight in insights
            .Where(i => i.Id   != "system.healthy"
                     && !i.Id.StartsWith("forecast.", StringComparison.Ordinal)
                     && i.Severity >= InsightSeverity.Recommendation)
            .OrderByDescending(i => (int)i.Severity)
            .ThenByDescending(i => i.ConfidencePercent))
        {
            var domain   = DomainFor(insight.Id);
            var evidence = BuildEvidence(insight);

            factors.Add(new CausalFactor(
                Title:               insight.Title,
                Explanation:         insight.Detail,
                Domain:              domain,
                ConfidencePercent:   insight.ConfidencePercent,
                Evidence:            evidence,
                AttributedProcesses: insight.AttributedProcesses,
                LinkedInsightId:     insight.Id));
        }

        return factors;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> BuildEvidence(SystemInsight insight)
    {
        var ev = new List<string>();

        // Primary evidence is the detail text
        if (!string.IsNullOrWhiteSpace(insight.Detail))
            ev.Add(insight.Detail);

        // Process attributions contribute named evidence
        foreach (var attr in insight.AttributedProcesses.Take(3))
            ev.Add($"{attr.DisplayName} is using {attr.UsageText} ({attr.KindLabel})");

        // Action hints give the user a next-step signal
        if (!string.IsNullOrWhiteSpace(insight.ActionHint))
            ev.Add($"Possible next step: {insight.ActionHint}");

        return ev;
    }

    internal static CausalDomain DomainFor(string insightId)
    {
        if (insightId.StartsWith("cpu.",      StringComparison.Ordinal)) return CausalDomain.Cpu;
        if (insightId.StartsWith("ram.",      StringComparison.Ordinal)) return CausalDomain.Memory;
        if (insightId.StartsWith("memory.",   StringComparison.Ordinal)) return CausalDomain.Memory;
        if (insightId.StartsWith("disk.",     StringComparison.Ordinal)) return CausalDomain.Disk;
        if (insightId.StartsWith("startup.",  StringComparison.Ordinal)) return CausalDomain.Startup;
        if (insightId.StartsWith("thermal.",  StringComparison.Ordinal)) return CausalDomain.Thermal;
        if (insightId.StartsWith("security.", StringComparison.Ordinal)) return CausalDomain.Security;
        if (insightId.StartsWith("process.",  StringComparison.Ordinal)) return CausalDomain.Process;
        return CausalDomain.Unknown;
    }
}
