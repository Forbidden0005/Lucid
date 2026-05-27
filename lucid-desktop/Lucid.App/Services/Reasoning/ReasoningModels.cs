namespace Lucid.Services.Reasoning;

// ── Evidence Graph types ───────────────────────────────────────────────────────

/// <summary>
/// Discriminated type for an evidence node in the operational evidence graph.
/// </summary>
public enum EvidenceNodeType
{
    /// <summary>A live hardware metric observation (CPU, RAM, Disk, Thermal).</summary>
    Telemetry       = 0,

    /// <summary>A rule-based intelligence finding (SystemInsight).</summary>
    Insight         = 1,

    /// <summary>A short-term behavioral anomaly detected by z-score comparison.</summary>
    Anomaly         = 2,

    /// <summary>A long-term drift observation from the Watchtower layer.</summary>
    Drift           = 3,

    /// <summary>A synthesized early warning from anomalies and Watchtower alerts.</summary>
    Warning         = 4,

    /// <summary>A completed remediation action from the operation history.</summary>
    Action          = 5,

    /// <summary>A forecast rule projection (resource exhaustion, thermal runaway, etc.).</summary>
    Forecast        = 6,

    /// <summary>A confirmed stabilization event following a remediation action.</summary>
    Stabilization   = 7,

    /// <summary>A specific process identified as contributing to a finding.</summary>
    Process         = 8,
}

/// <summary>
/// Relationship type between two evidence nodes.
/// All relationships are probabilistic (not claimed as certainty).
/// </summary>
public enum EvidenceRelationshipType
{
    /// <summary>Node A is a probable direct cause of Node B.</summary>
    Caused          = 0,

    /// <summary>Node A contributed to (but may not have solely caused) Node B.</summary>
    ContributedTo   = 1,

    /// <summary>Node A is a forecast projection of Node B's trajectory.</summary>
    Forecasted      = 2,

    /// <summary>Node B appears to have resolved the condition described by Node A.</summary>
    ResolvedBy      = 3,

    /// <summary>Node A and Node B co-occur at a rate suggesting shared causation.</summary>
    CorrelatedWith  = 4,

    /// <summary>Node A is being de-prioritized due to fatigue suppression by Node B.</summary>
    SuppressedBy    = 5,
}

/// <summary>
/// A single node in the operational evidence graph.
/// Each node represents a concrete, observed operational fact.
/// Confidence reflects data quality, not subjective assessment.
/// </summary>
public sealed record EvidenceNode
{
    /// <summary>Unique ID for this node within the graph (32-char hex).</summary>
    public required string          Id          { get; init; }

    /// <summary>Node type — determines glyph, color group, and display category.</summary>
    public required EvidenceNodeType NodeType   { get; init; }

    /// <summary>Short title shown in the evidence graph and explanation panels.</summary>
    public required string          Title       { get; init; }

    /// <summary>One-sentence summary of what this evidence observes.</summary>
    public required string          Summary     { get; init; }

    /// <summary>Confidence in this evidence item (0–1). Based on data availability.</summary>
    public required double          Confidence  { get; init; }

    /// <summary>When this evidence was observed or created.</summary>
    public required DateTimeOffset  CreatedAt   { get; init; }

    /// <summary>Optional ID linking back to the source object (insight ID, action ID, etc.).</summary>
    public          string?         SourceId    { get; init; }

    // ── Derived display helpers ────────────────────────────────────────────────

    /// <summary>Segoe MDL2 glyph for this node type.</summary>
    public string NodeGlyph => NodeType switch
    {
        EvidenceNodeType.Telemetry     => "", // activity
        EvidenceNodeType.Insight       => "", // lightbulb
        EvidenceNodeType.Anomaly       => "", // warning
        EvidenceNodeType.Drift         => "", // trending
        EvidenceNodeType.Warning       => "", // alert
        EvidenceNodeType.Action        => "", // gear/action
        EvidenceNodeType.Forecast      => "", // binoculars
        EvidenceNodeType.Stabilization => "", // checkmark
        EvidenceNodeType.Process       => "", // app window
        _                              => "",
    };

    /// <summary>Hex accent color for this node type.</summary>
    public string NodeColor => NodeType switch
    {
        EvidenceNodeType.Warning    => "#F87171",
        EvidenceNodeType.Anomaly    => "#FBBF24",
        EvidenceNodeType.Drift      => "#F59E0B",
        EvidenceNodeType.Forecast   => "#A78BFA",
        EvidenceNodeType.Action     => "#4E9FFF",
        EvidenceNodeType.Stabilization => "#34D399",
        EvidenceNodeType.Process    => "#94A3B8",
        EvidenceNodeType.Insight    => "#60A5FA",
        _                           => "#6B7280",
    };

    /// <summary>Confidence as a percentage string, e.g. "82%".</summary>
    public string ConfidenceLabel => $"{Confidence:0%}";
}

/// <summary>
/// A directed edge in the operational evidence graph.
/// Represents a causal, correlational, or temporal relationship between two nodes.
/// </summary>
public sealed record EvidenceEdge
{
    public required string                   FromNodeId        { get; init; }
    public required string                   ToNodeId          { get; init; }
    public required EvidenceRelationshipType RelationshipType  { get; init; }

    /// <summary>Confidence in this relationship (0–1).</summary>
    public required double                   Confidence        { get; init; }

    /// <summary>Human-readable relationship description for the explainability panel.</summary>
    public string RelationshipLabel => RelationshipType switch
    {
        EvidenceRelationshipType.Caused         => "likely caused",
        EvidenceRelationshipType.ContributedTo  => "contributed to",
        EvidenceRelationshipType.Forecasted     => "is forecasted to lead to",
        EvidenceRelationshipType.ResolvedBy     => "was resolved by",
        EvidenceRelationshipType.CorrelatedWith => "is correlated with",
        EvidenceRelationshipType.SuppressedBy   => "is suppressed by",
        _                                       => "is related to",
    };
}

/// <summary>
/// A complete evidence chain rooted at a primary cause node,
/// connecting through related nodes to downstream effects.
/// </summary>
public sealed record EvidenceChain
{
    public IReadOnlyList<EvidenceNode> Nodes    { get; init; } = [];
    public IReadOnlyList<EvidenceEdge> Edges    { get; init; } = [];
    public DateTimeOffset              ComputedAt { get; init; }
    public string                      RootNodeId { get; init; } = string.Empty;

    /// <summary>The root node (primary cause) if present in Nodes.</summary>
    public EvidenceNode? RootNode =>
        Nodes.FirstOrDefault(n => n.Id == RootNodeId);
}

// ── Root Cause Analysis types ─────────────────────────────────────────────────

/// <summary>
/// A candidate root cause for current system degradation.
/// All candidates are probabilistic — confidence scores reflect evidence strength.
/// </summary>
public sealed record RootCauseCandidate
{
    /// <summary>Short descriptive title of the suspected cause.</summary>
    public string                  CauseTitle           { get; init; } = string.Empty;

    /// <summary>Concise human-readable explanation of why this is suspected.</summary>
    public string                  Explanation          { get; init; } = string.Empty;

    /// <summary>List of evidence item summaries supporting this candidate.</summary>
    public IReadOnlyList<string>   SupportingEvidence   { get; init; } = [];

    /// <summary>Confidence in this candidate being the root cause (0–1).</summary>
    public double                  Confidence           { get; init; }

    /// <summary>Human-readable confidence label.</summary>
    public string                  ConfidenceLabel => Confidence switch
    {
        >= 0.75 => "High confidence",
        >= 0.50 => "Moderate confidence",
        >= 0.30 => "Low confidence",
        _       => "Uncertain",
    };

    /// <summary>Confidence color for UI display.</summary>
    public string                  ConfidenceColor => Confidence switch
    {
        >= 0.75 => "#34D399",
        >= 0.50 => "#F59E0B",
        _       => "#6B7280",
    };

    /// <summary>Names of affected system areas (e.g. "RAM", "Startup", "Disk").</summary>
    public IReadOnlyList<string>   AffectedSystems      { get; init; } = [];

    /// <summary>Dot-separated action IDs recommended to address this cause.</summary>
    public IReadOnlyList<string>   RecommendedActionIds { get; init; } = [];

    /// <summary>How many times a similar pattern was observed historically (0 = unknown).</summary>
    public int                     HistoricalOccurrences { get; init; }
}

// ── Evidence Explanation types ─────────────────────────────────────────────────

/// <summary>
/// Human-readable explainability record for a specific intelligence finding.
/// Every explanation must be grounded in concrete observed evidence.
/// </summary>
public sealed record EvidenceExplanation
{
    /// <summary>Single-sentence headline: what is happening right now.</summary>
    public string                  Headline        { get; init; } = string.Empty;

    /// <summary>Bullet-ready evidence items (each 1–2 sentences).</summary>
    public IReadOnlyList<string>   EvidenceItems   { get; init; } = [];

    /// <summary>What our confidence in this explanation is based on.</summary>
    public string                  ConfidenceSummary { get; init; } = string.Empty;

    /// <summary>Why this matters operationally — consequence if ignored.</summary>
    public string                  WhyItMatters    { get; init; } = string.Empty;

    /// <summary>Projected outcome if conditions continue unchanged.</summary>
    public string                  LikelyOutcome   { get; init; } = string.Empty;

    /// <summary>Concrete suggested next step for the operator.</summary>
    public string                  SuggestedNextStep { get; init; } = string.Empty;
}
