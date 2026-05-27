using Lucid.Services.Execution;
using Lucid.Helpers;

namespace Lucid.Services.Remediation;

// ── Trust level ────────────────────────────────────────────────────────────────

/// <summary>
/// User-configured automation trust level.
/// Controls how much autonomy the remediation system is allowed.
///
/// Philosophy:
///   Higher trust = less friction, but ALWAYS user-initiated workflows.
///   The platform NEVER auto-executes destructive actions regardless of trust level.
/// </summary>
public enum AutomationTrustLevel
{
    /// <summary>No automated execution. Workflows are planned but user runs each step.</summary>
    ManualOnly          = 0,

    /// <summary>User approves the full workflow plan before it starts.</summary>
    GuidedApproval      = 1,

    /// <summary>Workflow executes automatically but halts on any anomaly for review.</summary>
    SupervisedAutomation = 2,

    /// <summary>Same as supervised, but auto-triggers rollback on detected degradation.</summary>
    RecoveryAutomation  = 3,
}

// ── Workflow status ────────────────────────────────────────────────────────────

/// <summary>
/// Current lifecycle state of a workflow execution.
/// </summary>
public enum WorkflowStatus
{
    /// <summary>Workflow plan created but not yet submitted for approval.</summary>
    Draft              = 0,

    /// <summary>Awaiting explicit user approval to begin.</summary>
    AwaitingApproval   = 1,

    /// <summary>Actively executing steps.</summary>
    Running            = 2,

    /// <summary>Paused between phases waiting for stabilization.</summary>
    PausedForVerification = 3,

    /// <summary>All steps completed successfully.</summary>
    Completed          = 4,

    /// <summary>Workflow was rolled back to the pre-execution state.</summary>
    RolledBack         = 5,

    /// <summary>Workflow was halted due to a safety condition or anomaly.</summary>
    Halted             = 6,

    /// <summary>Workflow was cancelled by the user.</summary>
    Cancelled          = 7,

    /// <summary>Workflow failed due to an unexpected error.</summary>
    Failed             = 8,
}

// ── Workflow phase ────────────────────────────────────────────────────────────

/// <summary>
/// High-level execution phase within a workflow.
/// </summary>
public enum WorkflowPhase
{
    PreCheck       = 0,   // Safety validation before any changes
    Backup         = 1,   // State backup (startup snapshots, etc.)
    Execution      = 2,   // Core remediation actions
    Stabilization  = 3,   // Waiting for metrics to settle
    Validation     = 4,   // Before/after comparison
    Complete       = 5,
}

// ── Operational risk ──────────────────────────────────────────────────────────

/// <summary>
/// Estimated operational risk of a workflow.
/// Never uses alarm language — describes scope and reversibility.
/// </summary>
public enum OperationalRisk
{
    /// <summary>All steps are safe, reversible, and non-destructive.</summary>
    Minimal  = 0,

    /// <summary>Steps are reversible but may require a few minutes to take effect.</summary>
    Low      = 1,

    /// <summary>Some steps may require restart or have limited rollback.</summary>
    Moderate = 2,

    /// <summary>One or more steps require elevation or have bounded rollback.</summary>
    Elevated = 3,
}

// ── Step outcome ──────────────────────────────────────────────────────────────

/// <summary>
/// Outcome of a single workflow step execution.
/// </summary>
public enum StepOutcome
{
    Pending      = 0,
    Running      = 1,
    Succeeded    = 2,
    PartialSuccess = 3,
    Failed       = 4,
    Skipped      = 5,
    RolledBack   = 6,
}

// ── Stabilization result ──────────────────────────────────────────────────────

/// <summary>
/// Post-remediation stabilization assessment.
/// </summary>
public enum StabilizationResult
{
    /// <summary>System metrics measurably improved after the workflow.</summary>
    Improved      = 0,

    /// <summary>No significant change — system is stable but no improvement.</summary>
    Stable        = 1,

    /// <summary>System metrics worsened after the workflow (not necessarily causal).</summary>
    Degraded      = 2,

    /// <summary>Insufficient telemetry data to determine outcome.</summary>
    Inconclusive  = 3,
}

// ── Safety check status ───────────────────────────────────────────────────────

/// <summary>
/// Result of a safety pre-check before workflow or step execution.
/// </summary>
public enum SafetyCheckStatus
{
    /// <summary>All conditions met — safe to proceed.</summary>
    Passed  = 0,

    /// <summary>Conditions met but a caution applies — execution may continue.</summary>
    Caution = 1,

    /// <summary>A hard safety constraint is violated — execution must not proceed.</summary>
    Blocked = 2,
}

// ── Workflow step definition ──────────────────────────────────────────────────

/// <summary>
/// Definition of a single step within a remediation workflow.
/// Steps are data objects — execution is handled by WorkflowExecutionCoordinator.
/// </summary>
public sealed record WorkflowStepDefinition(
    string         StepId,
    string         Name,
    string         Description,
    string         ActionKey,
    WorkflowPhase  Phase,

    /// <summary>
    /// If this step fails, execution continues (the step is advisory).
    /// </summary>
    bool           IsOptional = false,

    /// <summary>
    /// If true, user must confirm this step even under SupervisedAutomation.
    /// </summary>
    bool           RequiresConfirmation = false,

    /// <summary>
    /// True when this action is known to require administrator elevation.
    /// The executor will return RequiresElevation if process is not elevated.
    /// </summary>
    bool           RequiresElevation = false,

    /// <summary>
    /// The action key to call for rollback, or null if this step is not rollbackable
    /// at the workflow level (executor rollback via token is separate).
    /// </summary>
    string?        RollbackActionKey = null,

    /// <summary>Optional parameters to pass to the executor.</summary>
    IReadOnlyDictionary<string, string>? Parameters = null);

// ── Remediation workflow ──────────────────────────────────────────────────────

/// <summary>
/// A complete remediation workflow consisting of ordered steps.
/// Workflows are pre-defined templates — never dynamically generated.
/// </summary>
public sealed record RemediationWorkflow(
    string         WorkflowId,
    string         Name,
    string         Description,
    string         Category,

    /// <summary>Ordered steps to execute in sequence.</summary>
    IReadOnlyList<WorkflowStepDefinition> Steps,

    /// <summary>Minimum trust level required to run this workflow.</summary>
    AutomationTrustLevel TrustLevelRequired,

    /// <summary>Estimated operational risk of this workflow.</summary>
    OperationalRisk Risk,

    /// <summary>True when all steps support rollback or have no lasting side effects.</summary>
    bool IsReversible,

    /// <summary>Estimated wall-clock duration in seconds.</summary>
    int EstimatedDurationSeconds,

    /// <summary>True when a system restart may be needed after completion.</summary>
    bool RequiresRestart,

    /// <summary>Short human-readable description of expected benefit.</summary>
    string ExpectedBenefit,

    /// <summary>Icon glyph for this workflow category.</summary>
    string Glyph);

// ── Plan suggestion ───────────────────────────────────────────────────────────

/// <summary>
/// A workflow suggestion from the planner, with rationale and triggering context.
/// </summary>
public sealed record RemediationPlanSuggestion(
    RemediationWorkflow Workflow,
    string              Rationale,
    int                 ConfidencePercent,
    IReadOnlyList<string> TriggeringInsightIds,
    string              ExpectedOutcomeText,
    DateTimeOffset      SuggestedAt);

// ── Stabilization snapshot ────────────────────────────────────────────────────

/// <summary>
/// Point-in-time system state snapshot for before/after comparison.
/// </summary>
public sealed record StabilizationSnapshot(
    double         CpuPercent,
    double         RamPercent,
    double         DiskPercent,
    double         CpuTemperatureCelsius,
    bool           TemperatureAvailable,
    int            ActiveInsightCount,
    IReadOnlyList<string> ActiveInsightIds,
    DateTimeOffset CapturedAt);

// ── Step execution record ─────────────────────────────────────────────────────

/// <summary>
/// Mutable record of a single step's execution progress within a workflow run.
/// </summary>
public sealed class StepExecutionRecord
{
    public string         StepId          { get; init; } = string.Empty;
    public string         StepName        { get; init; } = string.Empty;
    public string         ActionKey       { get; init; } = string.Empty;
    public StepOutcome    Outcome         { get; set; } = StepOutcome.Pending;
    public string?        StatusMessage   { get; set; }
    public string?        RollbackToken   { get; set; }
    public bool           CanRollback     { get; set; }
    public DateTimeOffset? StartedAt      { get; set; }
    public DateTimeOffset? CompletedAt    { get; set; }
    public TimeSpan       Duration        => (CompletedAt - StartedAt) ?? TimeSpan.Zero;
}

// ── Safety check result ───────────────────────────────────────────────────────

/// <summary>
/// Result of a safety constraint evaluation.
/// </summary>
public sealed record SafetyCheckResult(
    SafetyCheckStatus Status,
    string            Reason,
    string?           BlockedBy = null);

// ── Workflow execution state ──────────────────────────────────────────────────

/// <summary>
/// Live state of a workflow execution.
/// Updated in-place as the workflow progresses.
/// </summary>
public sealed class WorkflowExecutionState
{
    public string              ExecutionId       { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public string              WorkflowId        { get; init; } = string.Empty;
    public RemediationWorkflow Workflow          { get; init; } = null!;
    public WorkflowStatus      Status            { get; set; } = WorkflowStatus.Draft;
    public WorkflowPhase       CurrentPhase      { get; set; } = WorkflowPhase.PreCheck;
    public string              StatusText        { get; set; } = "Pending";
    public List<StepExecutionRecord> StepResults { get; init; } = [];
    public StabilizationSnapshot? BeforeSnapshot { get; set; }
    public StabilizationSnapshot? AfterSnapshot  { get; set; }
    public DateTimeOffset?     StartedAt         { get; set; }
    public DateTimeOffset?     CompletedAt       { get; set; }
    public string?             HaltReason        { get; set; }

    public int CurrentStepIndex => StepResults.Count(s =>
        s.Outcome is StepOutcome.Succeeded or StepOutcome.PartialSuccess
                  or StepOutcome.Failed or StepOutcome.Skipped);

    public double ProgressPercent => Workflow.Steps.Count == 0 ? 0.0
        : (double)CurrentStepIndex / Workflow.Steps.Count * 100.0;
}

// ── Remediation outcome report ────────────────────────────────────────────────

/// <summary>
/// Completed post-remediation analysis report.
/// Produced by RemediationOutcomeValidator after a workflow finishes.
/// </summary>
public sealed record RemediationOutcomeReport(
    string                 ExecutionId,
    string                 WorkflowName,
    WorkflowStatus         FinalStatus,
    StabilizationResult    StabilizationResult,
    double                 CpuChangePercent,
    double                 RamChangePercent,
    double                 DiskChangePercent,
    int                    InsightsResolved,
    string                 NarrativeSummary,
    StabilizationSnapshot? Before,
    StabilizationSnapshot? After,
    DateTimeOffset         CompletedAt);
