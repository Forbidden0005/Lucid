using Lucid.Services.Intelligence.Arbitration;
using Lucid.Services.Reasoning.Context;

namespace Lucid.Services.Reasoning.Cognitive;

/// <summary>
/// Unified output record of one complete reasoning pipeline run.
///
/// Captures everything produced by a single pass through the
/// cognitive reasoning engine and recommendation arbitrator:
/// inferences, clusters, timing, workload context, and any active
/// governance constraints.
///
/// Immutable — built once at the end of each pipeline cycle and
/// forwarded to subscribers, the diagnostics service, and the
/// Diagnostics page ViewModel.
///
/// Used by:
///   - ReasoningDiagnosticsService — attached to every CognitiveTraceRecord
///   - DiagnosticsPageViewModel    — drives the reasoning health panel
///   - CognitiveHealthMetrics      — feeds aggregate counters
/// </summary>
public sealed record ReasoningOutcome
{
    /// <summary>UTC time this outcome was produced.</summary>
    public DateTime ProducedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Wall-clock duration of the full reasoning + arbitration pipeline.</summary>
    public TimeSpan PipelineDuration { get; init; }

    /// <summary>All inferences produced by the cognitive engine, including suppressed ones.</summary>
    public IReadOnlyList<OperationalInference> AllInferences { get; init; } = [];

    /// <summary>Active (non-suppressed) inferences, ordered by priority descending.</summary>
    public IReadOnlyList<OperationalInference> ActiveInferences { get; init; } = [];

    /// <summary>Inferences suppressed by context or governance during this cycle.</summary>
    public IReadOnlyList<OperationalInference> SuppressedInferences { get; init; } = [];

    /// <summary>
    /// Recommendation clusters produced by the arbitrator, highest severity first.
    /// Empty when no arbitration was performed (e.g., no active inferences).
    /// </summary>
    public IReadOnlyList<RecommendationCluster> RecommendationClusters { get; init; } = [];

    /// <summary>Workload context active during this cycle. Null before first context synthesis.</summary>
    public ContextProfile? WorkloadContext { get; init; }

    /// <summary>
    /// Governance state annotation for this cycle.
    /// Non-null when a governance constraint was active — e.g. trust-posture tamper
    /// detected, degraded mode, or elevated operation-denial rate.
    /// Surfaced on the Diagnostics page for transparency.
    /// </summary>
    public string? GovernanceNote { get; init; }

    /// <summary>Names of inference rules that produced at least one inference this cycle.</summary>
    public IReadOnlyList<string> FiredRules { get; init; } = [];

    /// <summary>Names of inference rules evaluated but that produced no inferences.</summary>
    public IReadOnlyList<string> SkippedRules { get; init; } = [];

    // ── Computed ──────────────────────────────────────────────────────────────

    /// <summary>Full pipeline duration in milliseconds.</summary>
    public double PipelineDurationMs => PipelineDuration.TotalMilliseconds;

    /// <summary>Highest confidence score (0–1) across all active inferences.</summary>
    public double PeakConfidence =>
        ActiveInferences.Select(i => i.Confidence.Value).DefaultIfEmpty(0).Max();

    /// <summary>True when at least one active inference was produced this cycle.</summary>
    public bool HasActionableFindings => ActiveInferences.Count > 0;

    /// <summary>True when governance constraints affected this reasoning cycle.</summary>
    public bool WasGovernanceConstrained => GovernanceNote != null;

    /// <summary>Count of active (non-suppressed) inferences.</summary>
    public int ActiveInferenceCount => ActiveInferences.Count;

    /// <summary>Count of inferences suppressed during this cycle.</summary>
    public int SuppressedInferenceCount => SuppressedInferences.Count;
}
