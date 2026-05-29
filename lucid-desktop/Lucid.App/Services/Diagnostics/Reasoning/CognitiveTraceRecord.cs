using Lucid.Services.Reasoning.Cognitive;
using Lucid.Services.Reasoning.Context;

namespace Lucid.Services.Diagnostics.Reasoning;

/// <summary>
/// Immutable record capturing the full trace of one cognitive reasoning cycle.
///
/// A trace includes what was input (context), what was output (inferences),
/// how long it took, and what was suppressed — making the reasoning pipeline
/// fully observable and debuggable.
///
/// Used by <see cref="ReasoningDiagnosticsService"/> and the diagnostics page
/// to expose "why Lucid is thinking what it's thinking."
/// </summary>
public sealed record CognitiveTraceRecord
{
    /// <summary>Unique ID for this trace record.</summary>
    public Guid TraceId { get; init; } = Guid.NewGuid();

    /// <summary>UTC time the reasoning cycle started.</summary>
    public DateTime CycleStartedAtUtc { get; init; }

    /// <summary>Wall-clock duration of the full reasoning cycle.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>All inferences produced, including suppressed ones.</summary>
    public IReadOnlyList<OperationalInference> Inferences { get; init; } = [];

    /// <summary>Inferences that were suppressed by context (IsSuppressed = true).</summary>
    public IReadOnlyList<OperationalInference> SuppressedInferences { get; init; } = [];

    /// <summary>The context profile that was active during this cycle.</summary>
    public ContextProfile? Context { get; init; }

    /// <summary>Names of rules that fired (for diagnostics — not shown in UI).</summary>
    public IReadOnlyList<string> FiredRules { get; init; } = [];

    /// <summary>Names of rules that were evaluated but did not fire.</summary>
    public IReadOnlyList<string> SkippedRules { get; init; } = [];

    /// <summary>Governance state notes at the time of the cycle (e.g. "Degraded mode active").</summary>
    public string? GovernanceNote { get; init; }

    // ── Computed ──────────────────────────────────────────────────────────────

    /// <summary>Number of active (non-suppressed) inferences produced.</summary>
    public int ActiveInferenceCount => Inferences.Count(i => !i.IsSuppressed);

    /// <summary>Duration in milliseconds (for metrics).</summary>
    public double DurationMs => Duration.TotalMilliseconds;

    /// <summary>Highest confidence across all active inferences.</summary>
    public double MaxConfidence =>
        Inferences.Where(i => !i.IsSuppressed).Select(i => i.Confidence.Value).DefaultIfEmpty(0).Max();
}
