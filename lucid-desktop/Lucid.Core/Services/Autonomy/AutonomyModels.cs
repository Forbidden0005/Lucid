using Lucid.Services.Trust;

namespace Lucid.Services.Autonomy;

// ── Operational goal ──────────────────────────────────────────────────────────

/// <summary>
/// Discriminated type for operational goals the autonomous workflow engine can plan.
/// </summary>
public enum OperationalGoalType
{
    Unknown                   = 0,

    // ── Organization ──────────────────────────────────────────────────────
    DownloadsOrganization     = 1,
    ScreenshotArchival        = 2,
    DuplicateFileCleanup      = 3,
    MediaCategorization       = 4,
    GeneralFolderOrganization = 5,
    OldDownloadsCleanup       = 6,

    // ── Discovery ─────────────────────────────────────────────────────────
    ProjectDiscovery          = 10,
    RecentFilesDiscovery      = 11,
    LargeFileDiscovery        = 12,
    FolderSizeDiscovery       = 13,

    // ── Operational / investigation ───────────────────────────────────────
    StartupInvestigation      = 20,
    PerformanceInvestigation  = 21,
    StorageInvestigation      = 22,
    RepairWorkflow            = 23,
    NetworkInvestigation      = 24,

    // ── Guided cleanup ────────────────────────────────────────────────────
    CleanupGuide              = 30,
    MaintenanceGuide          = 31,
    TempFileCleanup           = 32,
    CacheCleanup              = 33,
}

/// <summary>
/// Structured operational goal produced by <see cref="OperationalGoalResolver"/>
/// from a conversational user request.
/// </summary>
public sealed record OperationalGoal
{
    /// <summary>Unique goal ID (32-char hex).</summary>
    public required string              Id              { get; init; }

    /// <summary>Resolved goal type.</summary>
    public required OperationalGoalType Type            { get; init; }

    /// <summary>Short display title.</summary>
    public required string              Title           { get; init; }

    /// <summary>Plain-English description of what the goal achieves.</summary>
    public required string              Description     { get; init; }

    /// <summary>Confidence in the goal resolution (0–100).</summary>
    public required int                 Confidence      { get; init; }

    /// <summary>The original user message that triggered this goal.</summary>
    public required string              OriginalRequest { get; init; }

    /// <summary>Optional structured parameters (e.g. folder path, age threshold).</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; }
        = new Dictionary<string, string>();

    public static string NewId() => Guid.NewGuid().ToString("N");
}

// ── Workflow step ─────────────────────────────────────────────────────────────

/// <summary>Functional type of a workflow step — determines narration and gating.</summary>
public enum WorkflowStepType
{
    /// <summary>Reads information — no state change.</summary>
    Discovery        = 0,

    /// <summary>Analyses collected data — no state change.</summary>
    Analysis         = 1,

    /// <summary>Builds a preview for user review — no state change.</summary>
    Preview          = 2,

    /// <summary>Requires explicit user approval before proceeding.</summary>
    RequiresApproval = 3,

    /// <summary>Performs a file operation (move, delete, archive).</summary>
    FileOperation    = 4,

    /// <summary>Runs a system operation via an executor.</summary>
    SystemOperation  = 5,

    /// <summary>Opens a folder or application — navigation only.</summary>
    Navigation       = 6,

    /// <summary>Produces a narration card with no execution.</summary>
    Narration        = 7,
}

/// <summary>
/// A single step within an <see cref="AutonomousWorkflow"/>.
/// Immutable once planned — the engine creates new instances rather than mutating.
/// </summary>
public sealed record WorkflowStep
{
    public required string            Id              { get; init; }
    public required string            Title           { get; init; }
    public required string            Description     { get; init; }
    public required WorkflowStepType  StepType        { get; init; }
    public required TrustRiskLevel    Risk            { get; init; }
    public required bool              RequiresApproval { get; init; }
    public required bool              IsReversible    { get; init; }

    /// <summary>Executor action ID, if this step delegates to the execution engine.</summary>
    public string?   ActionId        { get; init; }

    /// <summary>Path or target parameter (folder path, file path, etc.).</summary>
    public string?   TargetPath      { get; init; }

    /// <summary>Additional key-value parameters for the step executor.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; }
        = new Dictionary<string, string>();

    public static string NewId() => Guid.NewGuid().ToString("N");
}

// ── Workflow lifecycle ────────────────────────────────────────────────────────

/// <summary>Lifecycle status of an <see cref="AutonomousWorkflow"/>.</summary>
public enum WorkflowLifecycleStatus
{
    Planning,
    PendingReview,
    Approved,
    Running,
    Paused,
    CheckpointReached,
    Completed,
    RolledBack,
    Cancelled,
    Failed,
}

/// <summary>
/// A fully planned, multi-step autonomous workflow produced by
/// <see cref="WorkflowExecutionPlanner"/>.
///
/// Mutable status fields are updated in-place by <see cref="AutonomousWorkflowEngine"/>
/// as execution progresses. All step definitions remain immutable.
/// </summary>
public sealed class AutonomousWorkflow
{
    public required string                      Id             { get; init; }
    public required string                      Title          { get; init; }
    public required string                      Description    { get; init; }
    public required OperationalGoal             Goal           { get; init; }
    public required IReadOnlyList<WorkflowStep> Steps          { get; init; }
    public required TrustRiskLevel              HighestRisk    { get; init; }
    public required bool                        IsReversible   { get; init; }
    public required string                      ExpectedOutcome { get; init; }
    public required int                         EstimatedSeconds { get; init; }

    // ── Mutable execution state ───────────────────────────────────────────
    public WorkflowLifecycleStatus Status           { get; set; } = WorkflowLifecycleStatus.Planning;
    public int                     CurrentStepIndex { get; set; } = 0;
    public DateTimeOffset?         StartedAt        { get; set; }
    public DateTimeOffset?         CompletedAt      { get; set; }
    public string?                 FailureReason    { get; set; }
    public List<string>            CompletedStepIds { get; }      = [];

    public static string NewId() => Guid.NewGuid().ToString("N");
}

// ── File organization ─────────────────────────────────────────────────────────

/// <summary>
/// A proposed move or deletion for a single file, shown to the user in
/// the review preview before execution.
/// </summary>
public sealed record FileMoveProposal
{
    public required string SourcePath      { get; init; }
    public required string DestinationPath { get; init; }
    public required string Reason          { get; init; }
    public required long   FileSizeBytes   { get; init; }

    /// <summary>True if this proposal moves the file to the Recycle Bin (not a folder move).</summary>
    public required bool   IsRecycleDeletion { get; init; }

    public string FileName => Path.GetFileName(SourcePath);

    public string FormattedSize => FileSizeBytes switch
    {
        >= 1_073_741_824 => $"{FileSizeBytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{FileSizeBytes / 1_048_576.0:F1} MB",
        >= 1_024         => $"{FileSizeBytes / 1_024.0:F0} KB",
        _                => $"{FileSizeBytes} B",
    };
}

/// <summary>
/// A preview of all proposed file operations for a <see cref="FileOrganizationWorkflowService"/>
/// workflow. Shown to the user before any files are touched.
/// </summary>
public sealed record FileOrganizationPreview
{
    public required string                          WorkflowId    { get; init; }
    public required string                          FolderPath    { get; init; }
    public required IReadOnlyList<FileMoveProposal> Proposals     { get; init; }
    public required long                            TotalBytes    { get; init; }
    public required int                             TotalFiles    { get; init; }
    public required string                          PreviewSummary { get; init; }
    public required DateTimeOffset                  GeneratedAt   { get; init; }

    public bool IsApproved  { get; set; }
    public bool WasExecuted { get; set; }
}

// ── File discovery ────────────────────────────────────────────────────────────

/// <summary>A file found during a discovery workflow step.</summary>
public sealed record DiscoveredFile
{
    public required string         Path           { get; init; }
    public required string         Name           { get; init; }
    public required long           SizeBytes      { get; init; }
    public required DateTimeOffset LastModified   { get; init; }
    public string?                 Category       { get; init; }
    public string?                 RelevanceReason { get; init; }
}

/// <summary>A folder found during a discovery workflow step.</summary>
public sealed record DiscoveredFolder
{
    public required string Path            { get; init; }
    public required string Name            { get; init; }
    public required string RelevanceReason { get; init; }
    public          int    FileCount       { get; init; }
    public          long   TotalBytes      { get; init; }
}

/// <summary>
/// Full result of an <see cref="OperationalFileDiscoveryService"/> query.
/// </summary>
public sealed record FileDiscoveryResult
{
    public required string                       Id                { get; init; }
    public required OperationalGoalType          GoalType          { get; init; }
    public required IReadOnlyList<DiscoveredFile>   Files          { get; init; }
    public required IReadOnlyList<DiscoveredFolder> Folders        { get; init; }
    public required string                       SummaryNarration  { get; init; }
    public required DateTimeOffset               DiscoveredAt      { get; init; }
    public static string NewId() => Guid.NewGuid().ToString("N");
}

// ── Checkpoint ────────────────────────────────────────────────────────────────

/// <summary>
/// Snapshot of workflow state at a checkpoint.
/// Used by <see cref="WorkflowCheckpointManager"/> to support pause/resume/rollback.
/// </summary>
public sealed record WorkflowCheckpoint
{
    public required string                 WorkflowId           { get; init; }
    public required int                    StepIndex            { get; init; }
    public required WorkflowLifecycleStatus StatusAtCheckpoint  { get; init; }
    public required DateTimeOffset         CheckpointedAt       { get; init; }
    public required IReadOnlyList<string>  CompletedStepIds     { get; init; }
    public          string?                NarrationSummary     { get; init; }
}

// ── Review gate ───────────────────────────────────────────────────────────────

/// <summary>
/// Event args raised by <see cref="HumanReviewGate"/> when a workflow step
/// requires explicit user approval before execution proceeds.
/// </summary>
public sealed class WorkflowReviewEventArgs : EventArgs
{
    public required AutonomousWorkflow     Workflow        { get; init; }
    public required WorkflowStep           Step            { get; init; }
    public          FileOrganizationPreview? Preview       { get; init; }
    public required string                 ReviewNarration { get; init; }

    /// <summary>Call from UI thread to approve and continue execution.</summary>
    public Action? Approve { get; init; }

    /// <summary>Call from UI thread to deny and cancel the workflow.</summary>
    public Action? Deny    { get; init; }
}

// ── Progress events ───────────────────────────────────────────────────────────

/// <summary>Raised by <see cref="AutonomousWorkflowEngine"/> to drive UI progress display.</summary>
public sealed class WorkflowProgressEventArgs : EventArgs
{
    public required string WorkflowId    { get; init; }
    public required int    CurrentStep   { get; init; }
    public required int    TotalSteps    { get; init; }
    public required string StepTitle     { get; init; }
    public required string Narration     { get; init; }
    public required bool   IsCompleted   { get; init; }
    public required bool   IsFailed      { get; init; }
}

/// <summary>Raised when a workflow finishes, cancelled, or fails.</summary>
public sealed class WorkflowCompletedEventArgs : EventArgs
{
    public required string                 WorkflowId  { get; init; }
    public required WorkflowLifecycleStatus FinalStatus { get; init; }
    public required string                 Summary     { get; init; }
    public          string?                FailureReason { get; init; }
}
