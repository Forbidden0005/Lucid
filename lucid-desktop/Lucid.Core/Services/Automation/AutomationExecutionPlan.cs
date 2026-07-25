namespace Lucid.Services.Automation;

/// <summary>
/// A complete, reviewable automation plan.
///
/// A plan is ALWAYS shown to the user before any step executes.
/// The user sees the goal, every step, the highest risk level, and
/// whether rollback is available. Only after explicit approval does
/// the orchestrator begin executing steps.
///
/// Immutable record — the orchestrator produces updated copies as steps
/// transition through their lifecycle.
/// </summary>
public sealed record AutomationExecutionPlan
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>32-char lowercase hex ID.</summary>
    public required string Id              { get; init; }

    // ── Goal and narration ────────────────────────────────────────────────────

    /// <summary>Plain-English goal statement: "Open Task Manager to investigate CPU usage".</summary>
    public required string Goal            { get; init; }

    /// <summary>
    /// Full narration of what Lucid is about to do and why.
    /// Shown in the approval card before execution begins.
    /// </summary>
    public required string Narration       { get; init; }

    /// <summary>
    /// What the user can expect when the plan completes.
    /// "Task Manager will be open with the CPU column sorted by usage."
    /// </summary>
    public required string ExpectedOutcome { get; init; }

    // ── Steps ─────────────────────────────────────────────────────────────────

    public required IReadOnlyList<AutomationStep> Steps { get; init; }

    // ── Risk summary ──────────────────────────────────────────────────────────

    /// <summary>The highest risk level across all steps — drives the approval card style.</summary>
    public required AutomationRiskLevel HighestRisk { get; init; }

    /// <summary>
    /// True if at least one step requires explicit user confirmation.
    /// A plan with RequiresConfirmation=false still shows the approval card
    /// before executing, but may auto-advance through None/Low steps in
    /// <see cref="AutomationMode.SemiAutomatic"/> mode.
    /// </summary>
    public required bool RequiresConfirmation { get; init; }

    /// <summary>True if all executed steps can be rolled back.</summary>
    public required bool RollbackAvailable    { get; init; }

    // ── Metadata ──────────────────────────────────────────────────────────────

    public required AutomationScope  Scope            { get; init; }
    public required int              EstimatedSeconds { get; init; }

    /// <summary>Outcome confidence 0–100. Navigation plans score 95; complex repairs score lower.</summary>
    public          int              Confidence       { get; init; } = 90;

    // ── Status (updated by orchestrator) ─────────────────────────────────────

    public AutomationStatus Status       { get; init; } = AutomationStatus.Pending;
    public int              CurrentStep  { get; init; } = 0;
    public DateTimeOffset?  StartedAt    { get; init; }
    public DateTimeOffset?  CompletedAt  { get; init; }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Factory helper — new random plan ID.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>Fraction of steps completed: 0.0–1.0.</summary>
    public double Progress => Steps.Count == 0 ? 0 :
        (double)Steps.Count(s => s.Status is AutomationStatus.Completed or AutomationStatus.Failed)
        / Steps.Count;

    /// <summary>Number of steps completed so far.</summary>
    public int CompletedSteps =>
        Steps.Count(s => s.Status is AutomationStatus.Completed or AutomationStatus.Failed);

    /// <summary>True if every step has either completed or failed.</summary>
    public bool IsFinished =>
        Status is AutomationStatus.Completed or AutomationStatus.Cancelled
               or AutomationStatus.Failed or AutomationStatus.RolledBack;

    /// <summary>Human-readable risk summary for the approval card.</summary>
    public string RiskSummary => HighestRisk switch
    {
        AutomationRiskLevel.None   => "This plan only opens windows — nothing will change.",
        AutomationRiskLevel.Low    => "This plan opens system tools — nothing will change.",
        AutomationRiskLevel.Medium => "Some steps make reversible changes. Rollback is available.",
        AutomationRiskLevel.High   => "Review each step carefully. Some changes are hard to undo.",
        _                          => string.Empty,
    };
}
