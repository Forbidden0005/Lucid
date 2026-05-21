using ExplainMyPC.Services.Intelligence;
using ExplainMyPC.Services.Narrative;
using ExplainMyPC.Services.Timeline;

namespace ExplainMyPC.Services.Explain;

// ── Domain / severity enums ───────────────────────────────────────────────────

/// <summary>Which system domain a causal factor belongs to.</summary>
public enum CausalDomain
{
    Cpu,
    Memory,
    Disk,
    Startup,
    Thermal,
    Security,
    Process,
    Unknown,
}

/// <summary>How serious a forecast projection is.</summary>
public enum ForecastSeverity
{
    /// <summary>Trend is stable or improving — no concern.</summary>
    Stable,
    /// <summary>Worth monitoring — could develop into an issue.</summary>
    Watch,
    /// <summary>Likely to become a problem if left unaddressed.</summary>
    Warning,
    /// <summary>Forecast indicates an imminent constraint.</summary>
    Urgent,
}

/// <summary>What kind of evidence supports an explanation item.</summary>
public enum EvidenceKind
{
    DirectMeasurement,
    BaselineComparison,
    TrendObservation,
    ProcessAttribution,
    TimelineCorrelation,
}

// ── Section data models ───────────────────────────────────────────────────────

/// <summary>
/// A single contributing factor produced by the <see cref="OperationalReasoningGraph"/>.
/// Corresponds to one active insight mapped to a human-understandable causal domain.
/// Drives the "What's Causing This?" section.
/// </summary>
public sealed record CausalFactor(
    string                            Title,
    string                            Explanation,
    CausalDomain                      Domain,
    int                               ConfidencePercent,
    IReadOnlyList<string>             Evidence,
    IReadOnlyList<ProcessAttribution> AttributedProcesses,
    string?                           LinkedInsightId);

/// <summary>
/// A notable recent change derived from the operational timeline.
/// Drives the "What Changed?" section.
/// </summary>
public sealed record RecentChangeItem(
    string                Title,
    string                Detail,
    DateTimeOffset        OccurredAt,
    TimelineEventSeverity Severity,
    TimelineEventType     SourceType);

/// <summary>
/// A forward-looking projection derived from forecast-class insight rules.
/// Drives the "What Happens Next?" section.
/// </summary>
public sealed record ForecastOutlook(
    string           Title,
    string           Projection,
    ForecastSeverity Severity,
    string?          MitigationHint,
    int              ConfidencePercent);

/// <summary>
/// A ranked remediation recommendation with full reasoning context.
/// Drives the "Recommended Next Actions" section.
/// </summary>
public sealed record RecommendationReasoning(
    SystemAction Action,
    string       WhyItMatters,
    string       ExpectedBenefit,
    string       EstimatedTime,
    int          ConfidencePercent,
    bool         CanRollback,
    int          RankScore);

/// <summary>
/// One piece of evidence supporting the operational explanation.
/// Drives the "Why We Believe This" section.
/// </summary>
public sealed record EvidenceItem(
    string       Label,
    string       Value,
    EvidenceKind Kind);

// ── Top-level explanation ─────────────────────────────────────────────────────

/// <summary>
/// The complete output of <see cref="IExplainMyPcEngine"/> for one reasoning cycle.
///
/// This single record drives the entire Explain My PC page. All sections are
/// derived deterministically from live sensor data, intelligence findings, and
/// the operational timeline — no LLM or cloud service is involved.
/// </summary>
public sealed record OperationalExplanation(
    // Section 1 — System Summary Hero
    string                SystemHealthState,
    string                Headline,
    int                   OverallConfidencePercent,
    string                SummaryNarrative,
    IReadOnlyList<string> DominantCauses,

    // Section 2 — What Changed?
    IReadOnlyList<RecentChangeItem> RecentChanges,

    // Section 3 — What's Causing This?
    IReadOnlyList<CausalFactor> CausalFactors,

    // Section 4 — What Happens Next?
    IReadOnlyList<ForecastOutlook> Forecasts,

    // Section 5 — Recommended Next Actions
    IReadOnlyList<RecommendationReasoning> Recommendations,

    // Section 6 — Why We Believe This
    IReadOnlyList<EvidenceItem> Evidence,

    // Metadata
    Narrative.SystemHealthState HealthStateEnum,
    DateTimeOffset              GeneratedAt,
    int                         DataPointsConsidered);
