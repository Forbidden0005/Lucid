using ExplainMyPC.Intelligence.Models;
using ExplainMyPC.Intelligence.Rules;
using ExplainMyPC.Intelligence.Rules.Performance;
using ExplainMyPC.Intelligence.Rules.Startup;
using ExplainMyPC.Intelligence.Rules.Storage;
using ExplainMyPC.Intelligence.Scoring;
using ExplainMyPC.Intelligence.Summary;

namespace ExplainMyPC.Intelligence;

/// <summary>
/// Orchestrates the rules pipeline, scorer, and summary generator.
///
/// Rule evaluation order:
///   Rules are evaluated independently and in parallel (conceptually — the
///   implementation is sequential because rules are fast enough that Task
///   overhead would dominate). Order does not affect correctness.
///
/// Deduplication:
///   The MemoryPressureRule fires when both RAM is high AND disk I/O is high.
///   In that case, HighRamRule and HighDiskIoRule would also fire, producing
///   three issues with a shared root cause. The engine suppresses the less
///   specific rules when the composite pattern is detected, so the user sees
///   one clear explanation rather than three redundant warnings.
///
/// Recommendations:
///   Flattened from all fired rules, deduplicated by title, and ordered by
///   severity of the issuing rule. This gives the Repairs page a coherent
///   action list without repetition.
/// </summary>
public sealed class IntelligenceEngine : IIntelligenceEngine
{
    private readonly IReadOnlyList<IRule> _rules;

    public IntelligenceEngine()
    {
        _rules = BuildRuleSet();
    }

    public EngineOutput Analyze(SystemSnapshot snapshot)
    {
        var context = new RuleContext(snapshot);

        // Evaluate all rules; null return = no finding.
        var fired = _rules
            .Select(r => EvaluateSafe(r, context))
            .Where(r => r is not null)
            .Cast<RuleResult>()
            .OrderByDescending(r => (int)r.Severity)
            .ToList();

        // Suppress redundant sub-rules when the composite memory-pressure pattern fires.
        fired = SuppressRedundantRules(fired);

        var health   = HealthScorer.Compute(fired);
        var issues   = fired.Select(r => r.ToSystemIssue()).ToList();
        var recs     = FlattenRecommendations(fired);
        var summary  = SummaryGenerator.Generate(fired, health);

        return new EngineOutput(health, issues, recs, summary);
    }

    // ── Rule set ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<IRule> BuildRuleSet() =>
    [
        // Performance
        new HighCpuRule(),
        new HighRamRule(),
        new MemoryPressureRule(),
        new ThermalThrottleRule(),
        new HighGpuRule(),

        // Storage
        new LowDiskSpaceRule(),
        new HighDiskIoRule(),

        // Startup
        new StartupCongestionRule(),
    ];

    // ── Deduplication ─────────────────────────────────────────────────────────

    /// <summary>
    /// When MemoryPressureRule fires it already explains the RAM+Disk combination.
    /// Suppress the individual HighRamRule and HighDiskIoRule results to avoid
    /// showing three issues that all say the same thing.
    /// </summary>
    private static List<RuleResult> SuppressRedundantRules(List<RuleResult> fired)
    {
        if (!fired.Any(r => r.RuleId == "perf.memory.pressure"))
            return fired;

        return fired
            .Where(r => r.RuleId is not ("perf.ram.high" or "storage.disk.high_io"))
            .ToList();
    }

    // ── Recommendations ───────────────────────────────────────────────────────

    private static IReadOnlyList<Recommendation> FlattenRecommendations(List<RuleResult> fired)
    {
        var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Recommendation>();

        foreach (var rule in fired)
        {
            foreach (var rec in rule.Recommendations)
            {
                if (seen.Add(rec.Title))
                    result.Add(rec);
            }
        }

        return result;
    }

    // ── Safety wrapper ────────────────────────────────────────────────────────

    private static RuleResult? EvaluateSafe(IRule rule, RuleContext context)
    {
        try   { return rule.Evaluate(context); }
        catch { return null; }  // rule contract violation — never propagate
    }
}
