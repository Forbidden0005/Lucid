namespace Lucid.Services.Automation;

/// <summary>
/// The primary interface for the Phase 17D Desktop Automation Orchestrator.
///
/// The orchestrator coordinates guided, consent-driven automation:
///   • Plans are always shown to the user before any step executes
///   • Every step includes full narration and rationale
///   • High-risk steps always require explicit confirmation
///   • Execution is interruptible at any moment
///   • All events are raised on the UI thread
///
/// NEVER:
///   • Auto-execute without explicit user approval
///   • Suppress the confirmation card for high-risk steps
///   • Execute in ObserveOnly mode (produces guidance text only)
///   • Hide automation or narration from the user
///
/// Usage:
///   var plan = PlanFromGoal("investigate cpu", AutomationScope.SystemTool);
///   // Surface plan to the user (companion overlay approval card)
///   await ExecutePlanAsync(plan, ct);
/// </summary>
public interface IAutomationOrchestrator
{
    // ── Plan construction ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds a plan from a plain-English goal string using the pre-built workflow library.
    /// Falls back to a generic single-step navigation plan if no workflow matches.
    /// </summary>
    AutomationExecutionPlan PlanFromGoal(string goal, AutomationScope scope);

    /// <summary>
    /// Builds a navigation-only plan that opens a specific Lucid page.
    /// Always returns a zero-risk plan with a single navigation step.
    /// </summary>
    AutomationExecutionPlan PlanNavigation(string navigationTarget, string? goalOverride = null);

    /// <summary>
    /// Builds a plan that launches a named system tool or folder.
    /// Target: "task-manager", "device-manager", "event-viewer", "downloads",
    ///         "documents", "storage-settings", "startup-apps", "windows-security", etc.
    /// </summary>
    AutomationExecutionPlan PlanLaunch(string target);

    // ── Execution ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the plan step by step.
    /// Each step that requires confirmation pauses and raises <see cref="ConfirmationRequired"/>
    /// before executing. Execution resumes only after the user approves.
    ///
    /// Raises events on the UI thread as steps transition through their lifecycle.
    /// Returns when the plan completes, is cancelled, or fails.
    /// </summary>
    Task ExecutePlanAsync(AutomationExecutionPlan plan, CancellationToken ct = default);

    // ── Mode ──────────────────────────────────────────────────────────────────

    AutomationMode Mode { get; }
    void SetMode(AutomationMode mode);

    // ── Interrupt hooks (UI-thread callable) ──────────────────────────────────

    /// <summary>Pauses after the current step completes. Resumable.</summary>
    void Pause();

    /// <summary>Cancels remaining steps. Steps already completed remain in effect.</summary>
    void Cancel();

    /// <summary>Immediately cancels the in-progress step's CancellationToken.</summary>
    void EmergencyHalt();

    // ── State ─────────────────────────────────────────────────────────────────

    bool IsExecuting { get; }
    bool IsPaused    { get; }

    /// <summary>Resumes a paused plan. No-op if not paused.</summary>
    void Resume();

    // ── Events (all raised on UI thread) ─────────────────────────────────────

    /// <summary>Raised when a plan is approved and begins executing.</summary>
    event EventHandler<AutomationPlanEventArgs>? PlanStarted;

    /// <summary>Raised when a plan completes all steps successfully.</summary>
    event EventHandler<AutomationPlanEventArgs>? PlanCompleted;

    /// <summary>Raised when a plan is cancelled by the user.</summary>
    event EventHandler<AutomationPlanEventArgs>? PlanCancelled;

    /// <summary>Raised when a plan fails due to a step error.</summary>
    event EventHandler<AutomationPlanEventArgs>? PlanFailed;

    /// <summary>Raised immediately before a step executes (or before showing its confirmation card).</summary>
    event EventHandler<AutomationStepEventArgs>? StepStarted;

    /// <summary>Raised when a step completes successfully.</summary>
    event EventHandler<AutomationStepEventArgs>? StepCompleted;

    /// <summary>Raised when a step fails.</summary>
    event EventHandler<AutomationStepEventArgs>? StepFailed;

    /// <summary>
    /// Raised when a step requires user confirmation before executing.
    /// The handler must call <see cref="AutomationConfirmationRequestEventArgs.Approve"/>
    /// or <see cref="AutomationConfirmationRequestEventArgs.Deny"/> before returning.
    /// </summary>
    event EventHandler<AutomationConfirmationRequestEventArgs>? ConfirmationRequired;

    /// <summary>Raised whenever the narration text should be updated in the companion UI.</summary>
    event EventHandler<AutomationNarrationEventArgs>? NarrationUpdated;

    /// <summary>Raised when the automation mode changes.</summary>
    event EventHandler<AutomationMode>? ModeChanged;
}
