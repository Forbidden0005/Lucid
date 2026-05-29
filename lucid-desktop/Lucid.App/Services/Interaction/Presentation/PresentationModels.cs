using Lucid.Services.Intelligence.Arbitration;
using Lucid.Services.Reasoning.Cognitive;

namespace Lucid.Services.Interaction.Presentation;

/// <summary>
/// Unified presentation model for a single operational insight or inference.
///
/// This is the currency of the Interaction layer — a fully-formatted,
/// policy-compliant snapshot ready for direct UI binding. ViewModels consume
/// these records without performing any additional formatting.
///
/// Produced by <see cref="CognitivePresentationService"/> and
/// <see cref="OperationalInsightPresenter"/>.
/// </summary>
public sealed record OperationalInsightPresentation
{
    /// <summary>Unique identifier for this presentation record.</summary>
    public Guid PresentationId { get; init; } = Guid.NewGuid();

    /// <summary>UTC time this presentation was assembled.</summary>
    public DateTime AssembledAt { get; init; } = DateTime.UtcNow;

    // ── Display fields ────────────────────────────────────────────────────────

    /// <summary>Short, compact headline (≤ 120 chars). Policy-compliant.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Standard-length summary (≤ 300 chars). Policy-compliant.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Full detail text for expanded view. Policy-compliant, no length limit.</summary>
    public string? DetailText { get; init; }

    /// <summary>Trajectory / forecast sentence, if available.</summary>
    public string? TrajectoryText { get; init; }

    // ── Classification ────────────────────────────────────────────────────────

    /// <summary>Human-readable confidence label (e.g. "Strong signal").</summary>
    public string ConfidenceLabel { get; init; } = string.Empty;

    /// <summary>Confidence value in [0, 1].</summary>
    public double ConfidenceValue { get; init; }

    /// <summary>Human-readable severity label (e.g. "Needs attention").</summary>
    public string SeverityLabel { get; init; } = string.Empty;

    /// <summary>Design-system color token for the severity level.</summary>
    public string SeverityColorToken { get; init; } = "Severity.Info";

    /// <summary>Affected operational domains (e.g. ["Performance", "Thermal"]).</summary>
    public IReadOnlyList<string> Domains { get; init; } = [];

    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>True when this insight warrants elevated attention.</summary>
    public bool IsUrgent { get; init; }

    /// <summary>True when the insight has been suppressed by context or governance.</summary>
    public bool IsSuppressed { get; init; }

    /// <summary>Reason for suppression, if IsSuppressed is true.</summary>
    public string? SuppressionReason { get; init; }

    // ── Links ─────────────────────────────────────────────────────────────────

    /// <summary>IDs of related recommended actions (links to action surface).</summary>
    public IReadOnlyList<string> RelatedActionIds { get; init; } = [];

    /// <summary>Source inference ID for drill-down navigation.</summary>
    public Guid? SourceInferenceId { get; init; }
}

/// <summary>
/// Summary snapshot of the full cognitive presentation state.
/// Returned by CognitivePresentationService for dashboard binding.
/// </summary>
public sealed record CognitivePresentationSnapshot
{
    /// <summary>UTC time this snapshot was assembled.</summary>
    public DateTime AssembledAt { get; init; } = DateTime.UtcNow;

    /// <summary>All active (non-suppressed) insight presentations, priority-ordered.</summary>
    public IReadOnlyList<OperationalInsightPresentation> ActiveInsights { get; init; } = [];

    /// <summary>Suppressed insight presentations (for transparency panel).</summary>
    public IReadOnlyList<OperationalInsightPresentation> SuppressedInsights { get; init; } = [];

    /// <summary>True when the system is currently in a healthy, low-noise state.</summary>
    public bool IsHealthy => ActiveInsights.Count == 0;

    /// <summary>Total active insight count.</summary>
    public int ActiveCount => ActiveInsights.Count;

    /// <summary>Highest urgency across all active insights.</summary>
    public bool HasUrgentItems => ActiveInsights.Any(i => i.IsUrgent);
}
