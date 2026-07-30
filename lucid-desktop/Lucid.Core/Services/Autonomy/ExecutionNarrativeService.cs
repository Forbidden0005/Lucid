namespace Lucid.Services.Autonomy;

/// <summary>
/// Produces calm, operational narration strings for every phase of workflow execution.
///
/// Tone: transparent, professional, non-dramatic.
///
/// All methods are synchronous and pure — no I/O, no state mutations.
/// Thread-safe: all members are stateless.
/// </summary>
public sealed class ExecutionNarrativeService
{
    // ── Goal resolution ───────────────────────────────────────────────────────

    /// <summary>Narration when a goal is successfully resolved from a request.</summary>
    public string NarrateGoalResolved(OperationalGoal goal, int confidence) =>
        confidence >= 90
            ? $"I can help with that. Planning: {goal.Title}."
            : $"I think you want to {goal.Title.ToLowerInvariant()} (confidence: {confidence}%). " +
              "Let me build a plan.";

    /// <summary>Narration when the request cannot be mapped to a supported goal.</summary>
    public string NarrateGoalUnresolved(string request) =>
        $"I couldn't determine a specific operational goal from \"{request}\". " +
        "Try: \"organize my downloads\", \"find my project files\", or \"help with startup slowdown\".";

    // ── Planning ──────────────────────────────────────────────────────────────

    /// <summary>Narration while the plan is being built.</summary>
    public string NarratePlanningInProgress(OperationalGoal goal) =>
        $"Planning workflow: {goal.Title}…";

    /// <summary>Narration when the plan is ready for review.</summary>
    public string NarratePlanReady(AutonomousWorkflow workflow) =>
        $"Plan ready: {workflow.Steps.Count} step{(workflow.Steps.Count != 1 ? "s" : "")}, " +
        $"~{FormatDuration(workflow.EstimatedSeconds)}. Review the steps above.";

    // ── Safety validation ─────────────────────────────────────────────────────

    /// <summary>Narration when a step is blocked by the safety validator.</summary>
    public string NarrateValidationBlocked(string reason) =>
        $"This step was blocked for safety: {reason}";

    // ── Review gate ───────────────────────────────────────────────────────────

    /// <summary>Narration shown above the review card when approval is needed.</summary>
    public string NarrateReviewRequired(WorkflowStep step) =>
        step.StepType == WorkflowStepType.FileOperation
            ? $"Review needed before proceeding: {step.Title}. " +
              "Here is exactly what will happen:"
            : $"Confirm before running: {step.Title}.";

    /// <summary>Narration after the user approves a step.</summary>
    public string NarrateApproved(WorkflowStep step) =>
        $"Approved. Running: {step.Title}…";

    /// <summary>Narration after the user denies a step.</summary>
    public string NarrateDenied(WorkflowStep step) =>
        $"Understood — {step.Title} was not executed. Workflow stopped.";

    // ── Step execution ────────────────────────────────────────────────────────

    /// <summary>Narration when a discovery step begins.</summary>
    public string NarrateDiscoveryStarted(WorkflowStep step) =>
        $"Scanning: {step.Description}…";

    /// <summary>Narration when analysis is running.</summary>
    public string NarrateAnalysisStarted(WorkflowStep step) =>
        $"Analysing: {step.Description}…";

    /// <summary>Narration when a file preview is being built.</summary>
    public string NarratePreviewBuilding() =>
        "Preparing preview of what will change…";

    /// <summary>Narration when a preview is ready.</summary>
    public string NarratePreviewReady(FileOrganizationPreview preview) =>
        $"Preview ready: {preview.TotalFiles} file{(preview.TotalFiles != 1 ? "s" : "")} " +
        $"({FormatBytes(preview.TotalBytes)}). Review and approve to continue.";

    /// <summary>Narration when a step completes successfully.</summary>
    public string NarrateStepCompleted(WorkflowStep step) =>
        $"Done: {step.Title}.";

    /// <summary>Narration when a step fails.</summary>
    public string NarrateStepFailed(WorkflowStep step, string? reason) =>
        reason is not null
            ? $"{step.Title} could not be completed: {reason}"
            : $"{step.Title} could not be completed.";

    // ── Workflow completion ────────────────────────────────────────────────────

    /// <summary>Narration when the entire workflow completes successfully.</summary>
    public string NarrateWorkflowCompleted(AutonomousWorkflow workflow) =>
        $"Completed: {workflow.Title}. {workflow.ExpectedOutcome}";

    /// <summary>Narration when the workflow is cancelled by the user.</summary>
    public string NarrateWorkflowCancelled(int stepsCompleted) =>
        stepsCompleted == 0
            ? "Workflow cancelled — no changes were made."
            : $"Workflow cancelled after {stepsCompleted} completed step{(stepsCompleted != 1 ? "s" : "")}.";

    /// <summary>Narration when the workflow fails due to an error.</summary>
    public string NarrateWorkflowFailed(AutonomousWorkflow workflow, string? reason) =>
        reason is not null
            ? $"Workflow stopped: {reason}"
            : $"{workflow.Title} could not be completed.";

    // ── Checkpoint ────────────────────────────────────────────────────────────

    /// <summary>Narration when a checkpoint is saved.</summary>
    public string NarrateCheckpointSaved(int stepIndex, int totalSteps) =>
        $"Progress saved ({stepIndex}/{totalSteps} steps complete). " +
        "You can resume this workflow later.";

    /// <summary>Narration when resuming from a checkpoint.</summary>
    public string NarrateResumedFromCheckpoint(WorkflowCheckpoint cp) =>
        $"Resuming from step {cp.StepIndex + 1} (checkpoint from {FormatAge(cp.CheckpointedAt)}).";

    // ── Rollback ─────────────────────────────────────────────────────────────

    /// <summary>Narration when a rollback is initiated.</summary>
    public string NarrateRollbackStarted() =>
        "Rolling back completed steps…";

    /// <summary>Narration when rollback completes.</summary>
    public string NarrateRollbackCompleted(int stepsRolledBack) =>
        stepsRolledBack == 0
            ? "No changes to roll back."
            : $"Rollback complete — {stepsRolledBack} step{(stepsRolledBack != 1 ? "s were" : " was")} reversed.";

    // ── File discovery ────────────────────────────────────────────────────────

    /// <summary>Summary narration for a file discovery result.</summary>
    public string NarrateDiscoveryResult(FileDiscoveryResult result)
    {
        var files   = result.Files.Count;
        var folders = result.Folders.Count;

        if (files == 0 && folders == 0)
            return "No matching files or folders were found.";

        if (folders > 0 && files == 0)
            return $"Found {folders} folder{(folders != 1 ? "s" : "")} that may be relevant.";

        if (files > 0 && folders == 0)
            return $"Found {files} file{(files != 1 ? "s" : "")} that may be what you're looking for.";

        return $"Found {files} file{(files != 1 ? "s" : "")} and {folders} " +
               $"folder{(folders != 1 ? "s" : "")}.";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatDuration(int seconds) => seconds switch
    {
        < 60    => $"{seconds}s",
        < 3600  => $"{seconds / 60}m",
        _       => $"{seconds / 3600}h",
    };

    private static string FormatBytes(long b) => b switch
    {
        >= 1_073_741_824 => $"{b / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{b / 1_048_576.0:F1} MB",
        >= 1_024         => $"{b / 1_024.0:F0} KB",
        _                => $"{b} B",
    };

    private static string FormatAge(DateTimeOffset dt)
    {
        var age = DateTimeOffset.Now - dt;
        if (age.TotalMinutes < 2)  return "just now";
        if (age.TotalHours   < 1)  return $"{(int)age.TotalMinutes} minutes ago";
        if (age.TotalDays    < 1)  return $"{(int)age.TotalHours} hours ago";
        return $"{(int)age.TotalDays} days ago";
    }
}
