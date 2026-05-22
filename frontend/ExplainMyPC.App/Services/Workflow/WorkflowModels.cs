namespace ExplainMyPC.Services.Workflow;

/// <summary>
/// Priority tier for an operational workflow — drives sort order and visual treatment.
/// </summary>
public enum WorkflowPriority
{
    /// <summary>Conditions are actively degrading; action is time-sensitive.</summary>
    Critical = 0,

    /// <summary>A clear problem exists but is not yet escalating rapidly.</summary>
    High     = 1,

    /// <summary>A preventive or advisory workflow — act before it escalates.</summary>
    Medium   = 2,

    /// <summary>Informational — low urgency, no immediate risk.</summary>
    Low      = 3,
}

/// <summary>
/// A single step in a guided operational workflow.
/// Each step maps to a concrete, executable action.
/// </summary>
public sealed record WorkflowStep
{
    /// <summary>1-based sequential step number.</summary>
    public required int    StepNumber      { get; init; }

    /// <summary>Short imperative title, e.g. "Clear temporary files".</summary>
    public required string StepTitle       { get; init; }

    /// <summary>Two-to-four sentence explanation of what this step does and why.</summary>
    public required string StepDescription { get; init; }

    /// <summary>
    /// Dot-separated action ID for this step, e.g. "action.disk.clean-temp-files".
    /// Empty string when the step requires manual user action (e.g. "observe for 5 minutes").
    /// </summary>
    public required string ActionId        { get; init; }

    /// <summary>Expected effect after this step completes, e.g. "RAM usage should drop 5–10%."</summary>
    public required string ExpectedEffect  { get; init; }

    /// <summary>True when skipping this step is safe (precautionary or informational steps).</summary>
    public bool            IsOptional      { get; init; }

    /// <summary>Segoe MDL2 glyph to represent this step type.</summary>
    public string StepGlyph => string.IsNullOrEmpty(ActionId)
        ? "" // eye / observe
        : ""; // gear / action

    public bool HasActionId => !string.IsNullOrEmpty(ActionId);

    /// <summary>Display label for optional badge — "Optional" when step is optional, empty otherwise.</summary>
    public string OptionalLabel => IsOptional ? "Optional" : string.Empty;

    /// <summary>Visibility for optional badge — shown only when step is optional.</summary>
    public string OptionalLabelVisibility => IsOptional ? "Visible" : "Collapsed";
}

/// <summary>
/// A complete guided operational workflow produced by
/// <see cref="IOperationalWorkflowEngine"/>.
///
/// Workflows are evidence-driven and always explain:
///   WHY this workflow exists
///   WHAT evidence supports it
///   WHAT the expected outcome is
///   WHAT to do if it doesn't work
///
/// All success rates are historical — based on this machine's own remediation
/// outcome records. Cold-start workflows report 0.0 (unknown).
/// </summary>
public sealed record OperationalWorkflow
{
    /// <summary>Unique ID for this workflow instance (generated at build time).</summary>
    public required string                     Id                    { get; init; }

    /// <summary>Human-readable title, e.g. "High RAM Pressure Investigation".</summary>
    public required string                     Title                 { get; init; }

    /// <summary>
    /// One-to-two sentence summary of the operational problem this workflow addresses.
    /// Grounded in observed telemetry and findings — no speculation.
    /// </summary>
    public required string                     ProblemSummary        { get; init; }

    /// <summary>Summary of the evidence that triggered this workflow.</summary>
    public required string                     EvidenceSummary       { get; init; }

    /// <summary>Risk summary if no action is taken, e.g. "RAM exhaustion forecast within ~2 hours."</summary>
    public required string                     RiskSummary           { get; init; }

    /// <summary>Ordered list of steps the operator should follow.</summary>
    public required IReadOnlyList<WorkflowStep> Steps                { get; init; }

    /// <summary>Projected system state after completing all steps.</summary>
    public required string                     ExpectedOutcome       { get; init; }

    /// <summary>Actions to try if the primary workflow does not stabilize conditions.</summary>
    public required IReadOnlyList<string>      FallbackActions       { get; init; }

    /// <summary>
    /// Historical success rate for this workflow type (0–1).
    /// Derived from RecommendationEffectivenessProfile records.
    /// 0.0 means unknown / insufficient data.
    /// </summary>
    public required double                     HistoricalSuccessRate { get; init; }

    /// <summary>Priority tier — determines visual prominence and sort order.</summary>
    public required WorkflowPriority           Priority              { get; init; }

    /// <summary>When this workflow was built.</summary>
    public required DateTimeOffset             CreatedAt             { get; init; }

    // ── Derived display helpers ────────────────────────────────────────────────

    public string PriorityLabel => Priority switch
    {
        WorkflowPriority.Critical => "Critical",
        WorkflowPriority.High     => "High",
        WorkflowPriority.Medium   => "Medium",
        _                         => "Low",
    };

    public string PriorityColor => Priority switch
    {
        WorkflowPriority.Critical => "#F87171",
        WorkflowPriority.High     => "#FBBF24",
        WorkflowPriority.Medium   => "#60A5FA",
        _                         => "#6B7280",
    };

    public string PriorityBgColor => Priority switch
    {
        WorkflowPriority.Critical => "#1AF87171",
        WorkflowPriority.High     => "#1AFBBF24",
        WorkflowPriority.Medium   => "#1A60A5FA",
        _                         => "#0A6B7280",
    };

    public string SuccessRateLabel => HistoricalSuccessRate > 0
        ? $"{HistoricalSuccessRate:0%} success rate"
        : "No historical data";

    public string SuccessRateColor => HistoricalSuccessRate switch
    {
        >= 0.7 => "#34D399",
        >= 0.4 => "#F59E0B",
        > 0    => "#F87171",
        _      => "#6B7280",
    };

    public string StepCountLabel =>
        $"{Steps.Count} step{(Steps.Count == 1 ? "" : "s")}";
}
