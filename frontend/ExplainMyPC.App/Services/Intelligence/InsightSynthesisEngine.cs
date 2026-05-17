namespace ExplainMyPC.Services.Intelligence;

/// <summary>
/// Runs all registered <see cref="ISynthesisRule"/> heuristics against the
/// current set of active base findings and returns the resulting compound insights.
///
/// Called by <see cref="SystemInsightEngine"/> after Phase 1 (base rule evaluation)
/// completes. Results are merged back into the engine's active insight list so they
/// participate in the existing change-detection and severity-sort logic.
///
/// Fault isolation:
///   Each synthesis rule is wrapped in a try/catch. A crashing rule is silently
///   skipped; other rules and the base findings are unaffected.
///
/// Deduplication:
///   If two synthesis rules independently produce an insight with the same ID
///   (unexpected but possible in future multi-rule configurations), the later
///   duplicate is discarded via DistinctBy(Id) before returning.
/// </summary>
public sealed class InsightSynthesisEngine
{
    private readonly IReadOnlyList<ISynthesisRule> _rules;

    public InsightSynthesisEngine(IReadOnlyList<ISynthesisRule> rules)
    {
        _rules = rules;
    }

    /// <summary>
    /// Evaluates all synthesis rules and returns the combined, deduplicated
    /// set of compound insights for the current tick.
    /// </summary>
    /// <param name="baseInsights">
    ///     Active base findings from this tick's rule evaluation.
    ///     Must not contain previously synthesized insights.
    /// </param>
    public IReadOnlyList<SystemInsight> Synthesize(IReadOnlyList<SystemInsight> baseInsights)
    {
        // Skip synthesis entirely when there is only one base finding — there is
        // nothing to correlate, and SystemRunningWellRule fires alone when healthy.
        if (baseInsights.Count < 2)
            return [];

        var results = new List<SystemInsight>();

        foreach (var rule in _rules)
        {
            try
            {
                var findings = rule.Evaluate(baseInsights);
                if (findings.Count > 0)
                    results.AddRange(findings);
            }
            catch
            {
                // Synthesis failure is best-effort — never disrupts base findings.
            }
        }

        if (results.Count == 0)
            return [];

        // Guard against duplicate IDs from multiple rules (unlikely but defensive).
        return results.DistinctBy(i => i.Id).ToList();
    }
}
