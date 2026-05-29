using Lucid.Services.Intelligence.Arbitration;

namespace Lucid.Services.Diagnostics.Reasoning;

/// <summary>
/// Immutable record capturing the arbitration decision made for a single action
/// during one arbitration cycle.
///
/// Provides a full audit trail for the arbitration pipeline:
/// "Why was this recommendation suppressed / shown / merged?"
///
/// Used by <see cref="ReasoningDiagnosticsService"/> and the diagnostics page.
/// </summary>
public sealed record ArbitrationDecisionRecord
{
    /// <summary>UTC time this arbitration decision was made.</summary>
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>The dot-separated action ID that was evaluated (e.g. "action.startup.disable").</summary>
    public required string ActionId { get; init; }

    /// <summary>The insight that sourced this action.</summary>
    public required string SourceInsightId { get; init; }

    /// <summary>Whether this action passed all arbitration filters and was surfaced.</summary>
    public required bool Passed { get; init; }

    /// <summary>Severity classification assigned to this action.</summary>
    public required RecommendationSeverity Severity { get; init; }

    /// <summary>
    /// Human-readable reason for suppression when <see cref="Passed"/> is false.
    /// Null when the action passed.
    /// </summary>
    public string? SuppressReason { get; init; }

    /// <summary>The cluster domain this action was assigned to (if passed).</summary>
    public string? AssignedDomain { get; init; }

    /// <summary>Priority score at the time of arbitration.</summary>
    public double PriorityScore { get; init; }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>UI-friendly outcome label.</summary>
    public string OutcomeLabel => Passed
        ? $"Surfaced in {AssignedDomain ?? "General"}"
        : $"Suppressed: {SuppressReason ?? "Unknown reason"}";
}
