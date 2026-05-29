using System.Collections.Concurrent;
using Lucid.Services.Intelligence;
using Lucid.Services.Learning;
using Lucid.Services.Reasoning.Context;
using Lucid.Services.Reasoning.Cognitive;

namespace Lucid.Services.Intelligence.Arbitration;

/// <summary>
/// Production implementation of <see cref="IRecommendationArbitrator"/>.
///
/// Arbitration pipeline per cycle:
///   1. Collect all actions from all active insights.
///   2. Score and rank using <see cref="IGlobalRecommendationPrioritizer"/>.
///   3. Apply deduplication window — skip actions shown recently.
///   4. Apply context suppression via <see cref="ContextSuppressionEngine"/>.
///   5. Apply alert fatigue check via <see cref="IAlertFatigueManager"/>.
///   6. Cluster surviving actions by domain.
///   7. Return sorted clusters (Critical → Cosmetic).
///
/// Thread-safe: deduplication state uses a concurrent dictionary.
/// </summary>
public sealed class RecommendationArbitrator : IRecommendationArbitrator
{
    private readonly IGlobalRecommendationPrioritizer _prioritizer;
    private readonly IAlertFatigueManager             _fatigueManager;
    private readonly IRemediationLearningService      _learning;

    // Tracks last-shown timestamp per action ID for deduplication windowing.
    private readonly ConcurrentDictionary<string, DateTime> _lastShown = new();

    // Suppressed items from the most recent arbitration cycle.
    private volatile IReadOnlyList<(string, string)> _lastCycleSuppressed = [];

    public RecommendationArbitrator(
        IGlobalRecommendationPrioritizer prioritizer,
        IAlertFatigueManager             fatigueManager,
        IRemediationLearningService      learning)
    {
        _prioritizer    = prioritizer;
        _fatigueManager = fatigueManager;
        _learning       = learning;
    }

    // ── IRecommendationArbitrator ─────────────────────────────────────────────

    /// <inheritdoc />
    public IReadOnlyList<RecommendationCluster> Arbitrate(
        IReadOnlyList<SystemInsight>        insights,
        ContextProfile?                     context,
        IReadOnlyList<OperationalInference> activeInferences)
    {
        if (insights.Count == 0) return [];

        var suppressedLog = new List<(string, string)>();

        // Step 1 + 2: Rank all actions.
        var ranked = _prioritizer.Rank(insights, _learning);

        // Step 3–5: Filter through deduplication + context suppression + fatigue.
        var surviving = new List<PrioritizedAction>();
        foreach (var action in ranked)
        {
            // SystemAction.Id is the stable dot-separated identifier (e.g. "action.startup.disable").
            var actionId = action.Action.Id;

            // Severity classification — insight ID prefix carries category info
            // (e.g. "cpu.sustained-high" → contains "cpu").
            var severity = RecommendationSeverityModel.Classify(
                action.SourceInsight.Id,
                action.Action.Id);

            // Deduplication window check.
            if (_lastShown.TryGetValue(actionId, out var lastShown))
            {
                var window = RecommendationSeverityModel.DeduplicationWindow(severity);
                if (DateTime.UtcNow - lastShown < window)
                {
                    suppressedLog.Add((actionId, $"Deduplication window ({window.TotalMinutes:0}m)"));
                    continue;
                }
            }

            // Context suppression check — pass insight ID as the category hint.
            var (isSuppressed, reason) = ContextSuppressionEngine.Evaluate(
                action.Action,
                action.SourceInsight.Id,
                context);

            if (isSuppressed)
            {
                suppressedLog.Add((actionId, reason ?? "Context suppressed"));
                continue;
            }

            // Alert fatigue check — factor below 0.5 means heavily suppressed.
            if (_fatigueManager.GetFatigueFactor(actionId, AlertTolerance.Medium) < 0.5)
            {
                suppressedLog.Add((actionId, "Alert fatigue suppressed"));
                continue;
            }

            surviving.Add(action);
        }

        _lastCycleSuppressed = suppressedLog.AsReadOnly();

        if (surviving.Count == 0) return [];

        // Step 6: Cluster by domain.
        var clusters = ClusterByDomain(surviving, activeInferences);

        // Step 7: Sort clusters — Critical first.
        return [.. clusters.OrderByDescending(c => (int)c.MaxSeverity)];
    }

    /// <inheritdoc />
    public IReadOnlyList<(string ActionId, string Reason)> GetLastCycleSuppressed() =>
        _lastCycleSuppressed;

    // ── Clustering ────────────────────────────────────────────────────────────

    private List<RecommendationCluster> ClusterByDomain(
        List<PrioritizedAction>             actions,
        IReadOnlyList<OperationalInference> inferences)
    {
        // Group by domain derived from insight ID prefix (e.g. "cpu.sustained-high" → "Performance").
        var byDomain = actions.GroupBy(a => ExtractDomain(a.SourceInsight.Id));

        var clusters = new List<RecommendationCluster>();

        foreach (var group in byDomain)
        {
            var members     = group.OrderByDescending(a => a.PriorityScore).ToList();
            var lead        = members[0];
            var maxSeverity = members.Max(a =>
                RecommendationSeverityModel.Classify(
                    a.SourceInsight.Id, a.Action.Id));

            // Find related inference IDs for this domain.
            var relatedInferences = inferences
                .Where(inf => inf.Domains.Any(d =>
                    string.Equals(d, group.Key, StringComparison.OrdinalIgnoreCase)))
                .Select(inf => inf.InferenceId.ToString())
                .ToList();

            var headline = members.Count == 1
                ? lead.Action.Title ?? group.Key
                : $"{group.Key} — {members.Count} actions available";

            clusters.Add(new RecommendationCluster
            {
                Headline            = headline,
                Domain              = group.Key,
                MaxSeverity         = maxSeverity,
                LeadAction          = lead,
                AllActions          = members.AsReadOnly(),
                RelatedInferenceIds = relatedInferences.AsReadOnly(),
            });

            // Record last-shown time for all surviving actions.
            foreach (var a in members)
            {
                if (!string.IsNullOrEmpty(a.Action.Id))
                    _lastShown[a.Action.Id] = DateTime.UtcNow;
            }
        }

        return clusters;
    }

    // Derives a display domain from an insight ID (e.g. "cpu.sustained-high" → "Performance").
    private static string ExtractDomain(string insightId)
    {
        if (string.IsNullOrEmpty(insightId)) return "General";
        var lower = insightId.ToLowerInvariant();
        return lower switch
        {
            var s when s.Contains("startup") => "Startup",
            var s when s.Contains("cpu")     => "Performance",
            var s when s.Contains("ram")     => "Performance",
            var s when s.Contains("thermal") => "Thermal",
            var s when s.Contains("storage") => "Storage",
            var s when s.Contains("disk")    => "Storage",
            var s when s.Contains("repair")  => "Repairs",
            var s when s.Contains("network") => "Network",
            _ => TitleCase(insightId.Contains('.') ? insightId[..insightId.IndexOf('.')] : insightId),
        };
    }

    private static string TitleCase(string s) =>
        s.Length > 0
            ? char.ToUpperInvariant(s[0]) + s[1..]
            : s;
}
