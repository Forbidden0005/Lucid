namespace Lucid.Services.Automation;

// ── Scope ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Classifies what domain an automation action operates in.
/// Used to route to the correct automation service and determine
/// the default risk level and confirmation requirements.
/// </summary>
public enum AutomationScope
{
    /// <summary>Opens a folder or navigates File Explorer. Zero-risk, no system state change.</summary>
    Navigation      = 0,

    /// <summary>Opens a system tool (Task Manager, Device Manager, Settings). Zero-risk.</summary>
    SystemTool      = 1,

    /// <summary>
    /// Runs a reversible cleanup or repair (temp files, startup item, cache clear).
    /// Medium risk — changes system state but rollback is available.
    /// </summary>
    Remediation     = 2,

    /// <summary>
    /// Changes a system setting (startup policy, network, registry).
    /// High risk — always requires explicit user confirmation.
    /// </summary>
    SystemChange    = 3,

    /// <summary>
    /// Operates on user files (previews only — NO silent moves/deletes).
    /// Always shows a preview and requires confirmation before any file operation.
    /// </summary>
    FileOperation   = 4,
}

// ── Risk level ────────────────────────────────────────────────────────────────

/// <summary>
/// Risk classification for an automation step.
/// Drives confirmation requirements and narration prominence.
/// </summary>
public enum AutomationRiskLevel
{
    /// <summary>Pure navigation — opens a window or folder. Nothing changes.</summary>
    None    = 0,

    /// <summary>Opens a system tool. No settings or files are modified.</summary>
    Low     = 1,

    /// <summary>Reversible change — cache clear, startup entry toggle. Can be undone.</summary>
    Medium  = 2,

    /// <summary>Significant system change — repair, registry, network reset. Harder to undo.</summary>
    High    = 3,
}

// ── Automation mode ───────────────────────────────────────────────────────────

/// <summary>
/// Controls the level of automation Lucid is permitted to perform.
/// The user sets this; it cannot be changed by observed content or automated logic.
///
/// Default: <see cref="ConfirmBeforeAction"/> — companion prepares each step,
/// user confirms before execution.
/// </summary>
public enum AutomationMode
{
    /// <summary>
    /// Lucid only explains and describes. No automation whatsoever.
    /// Every "action" produces guidance text only — the user does everything.
    /// </summary>
    ObserveOnly,

    /// <summary>
    /// Lucid walks the user through steps interactively.
    /// Each step is narrated and the user clicks "Do it" themselves.
    /// </summary>
    GuidedAssist,

    /// <summary>
    /// Lucid prepares each step and shows a confirmation card before executing.
    /// User must approve every step, regardless of risk level.
    /// This is the recommended default.
    /// </summary>
    ConfirmBeforeAction,

    /// <summary>
    /// Low-risk steps (None/Low) execute automatically after narration.
    /// Medium and High risk steps always require explicit confirmation.
    /// The user must opt into this mode deliberately.
    /// </summary>
    SemiAutomatic,
}

// ── Status ────────────────────────────────────────────────────────────────────

/// <summary>Lifecycle state of an automation plan or individual step.</summary>
public enum AutomationStatus
{
    /// <summary>Created but not yet shown to the user or approved.</summary>
    Pending,

    /// <summary>User has reviewed and approved the plan/step.</summary>
    Approved,

    /// <summary>Currently executing.</summary>
    Running,

    /// <summary>Paused at the user's request — can be resumed.</summary>
    Paused,

    /// <summary>Successfully completed.</summary>
    Completed,

    /// <summary>Cancelled by the user before completion.</summary>
    Cancelled,

    /// <summary>Failed due to an error during execution.</summary>
    Failed,

    /// <summary>Completed steps were rolled back after a failure or user request.</summary>
    RolledBack,
}

// ── Event args ────────────────────────────────────────────────────────────────

/// <summary>Event args raised when an automation step transitions state.</summary>
public sealed class AutomationStepEventArgs : EventArgs
{
    public required AutomationStep Step    { get; init; }
    public          string?        Message { get; init; }
    public          string?        Error   { get; init; }
}

/// <summary>
/// Event args raised when a step requires explicit user confirmation before executing.
/// Subscribe to <see cref="IAutomationOrchestrator.ConfirmationRequired"/> to handle this.
/// </summary>
public sealed class AutomationConfirmationRequestEventArgs : EventArgs
{
    public required AutomationExecutionPlan Plan       { get; init; }
    public required AutomationStep          Step       { get; init; }
    public required string                  Narration  { get; init; }
    public required string                  RiskLabel  { get; init; }
    public required bool                    CanRollback { get; init; }

    /// <summary>
    /// Call this to approve the step and allow execution to proceed.
    /// Must be called from the UI thread.
    /// </summary>
    public Action? Approve   { get; init; }

    /// <summary>
    /// Call this to deny and cancel the remaining plan.
    /// Must be called from the UI thread.
    /// </summary>
    public Action? Deny      { get; init; }
}

/// <summary>Event args carrying a narration update from the automation engine.</summary>
public sealed class AutomationNarrationEventArgs : EventArgs
{
    public required string Message    { get; init; }
    public          bool   IsProgress { get; init; }
    public          bool   IsWarning  { get; init; }
}

/// <summary>Event args raised when an automation plan completes or is cancelled.</summary>
public sealed class AutomationPlanEventArgs : EventArgs
{
    public required AutomationExecutionPlan Plan   { get; init; }
    public          string?                 Reason { get; init; }
}

// ── Organization preview (FileOperation scope) ────────────────────────────────

/// <summary>
/// A read-only preview of what a file organization operation WOULD do.
/// Never executes moves or deletes — only surfaces information so the user
/// can decide whether to proceed manually.
/// </summary>
public sealed record OrganizationPreview
{
    public required string FolderPath    { get; init; }
    public required int    TotalFiles    { get; init; }
    public required long   TotalBytes    { get; init; }

    /// <summary>Categories of files found and their counts/sizes.</summary>
    public required IReadOnlyList<OrganizationCategory> Categories { get; init; }

    public string Summary =>
        $"{TotalFiles} files ({FormatBytes(TotalBytes)}) in {Categories.Count} categories.";

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024         => $"{bytes / 1_024.0:F0} KB",
        _                => $"{bytes} B",
    };
}

/// <summary>A file category within an organization preview.</summary>
public sealed record OrganizationCategory
{
    public required string CategoryName  { get; init; }
    public required int    FileCount     { get; init; }
    public required long   TotalBytes    { get; init; }
    public required string ExampleExtensions { get; init; }
}
