namespace Lucid.Services.Automation;

/// <summary>
/// An individual step within an <see cref="AutomationExecutionPlan"/>.
///
/// Immutable record — a new instance is created when the step changes status.
/// The orchestrator produces an updated copy and re-raises the relevant event.
///
/// Every step carries full narration so the user always knows:
///   • What will happen (<see cref="Title"/>)
///   • Why it's needed (<see cref="Rationale"/>)
///   • What to expect afterward (<see cref="ExpectedOutcome"/>)
///   • How to undo it if needed (<see cref="HowToUndo"/>)
/// </summary>
public sealed record AutomationStep
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>32-char lowercase hex ID, unique within the plan.</summary>
    public required string Id            { get; init; }

    /// <summary>Human-readable step title shown in the confirmation card.</summary>
    public required string Title         { get; init; }

    // ── Narration (always present) ────────────────────────────────────────────

    /// <summary>
    /// Full narration sentence: "Opening Task Manager so you can see which process
    /// is consuming the most CPU right now."
    /// Always written in plain English, first-person from Lucid's perspective.
    /// </summary>
    public required string Narration     { get; init; }

    /// <summary>
    /// Why this step is necessary: "Task Manager surfaces real-time CPU attribution
    /// which lets you identify and stop the offending process."
    /// </summary>
    public required string Rationale     { get; init; }

    // ── Execution ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps to a registered <see cref="Lucid.Services.Execution.IActionExecutor"/>
    /// action ID, or to a built-in <see cref="AutomationScope"/> handler if empty.
    /// </summary>
    public required string ActionId      { get; init; }

    /// <summary>Additional parameters passed to the executor or handler.</summary>
    public          string? ActionParams { get; init; }

    // ── Risk &amp; safety ──────────────────────────────────────────────────────

    public required AutomationRiskLevel Risk               { get; init; }
    public required bool                RequiresConfirmation { get; init; }

    /// <summary>True if this step can be undone after execution.</summary>
    public          bool                RollbackAvailable  { get; init; }

    /// <summary>
    /// What the user will see after this step completes.
    /// "Task Manager will open and highlight the CPU column."
    /// </summary>
    public          string?             ExpectedOutcome    { get; init; }

    /// <summary>
    /// How to reverse this step if the user changes their mind.
    /// Null for irreversible steps (which always require High risk classification).
    /// </summary>
    public          string?             HowToUndo          { get; init; }

    // ── Status (updated by orchestrator) ─────────────────────────────────────

    public AutomationStatus Status      { get; init; } = AutomationStatus.Pending;
    public DateTimeOffset?  ExecutedAt  { get; init; }
    public string?          ExecutionResult { get; init; }
    public string?          ErrorMessage   { get; init; }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Factory helper — new random step ID.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>Risk badge label shown in the confirmation card.</summary>
    public string RiskLabel => Risk switch
    {
        AutomationRiskLevel.None   => "Safe",
        AutomationRiskLevel.Low    => "Low risk",
        AutomationRiskLevel.Medium => "Reversible",
        AutomationRiskLevel.High   => "Review carefully",
        _                          => "Unknown",
    };

    /// <summary>Returns a copy of this step with an updated status.</summary>
    public AutomationStep WithStatus(
        AutomationStatus status,
        string?          result = null,
        string?          error  = null) => this with
        {
            Status          = status,
            ExecutedAt      = status is AutomationStatus.Running or AutomationStatus.Completed
                              ? DateTimeOffset.Now
                              : ExecutedAt,
            ExecutionResult = result ?? ExecutionResult,
            ErrorMessage    = error  ?? ErrorMessage,
        };
}
