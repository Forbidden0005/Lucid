using Lucid.Services.Execution.Validation;

namespace Lucid.Services.Autonomy;

/// <summary>
/// Validates workflow steps and file operations against hard safety rules.
///
/// Blocks:
///   • Windows system directories (Windows\, System32\, Program Files\, etc.)
///   • User credential / browser profile directories
///   • Hidden system data directories (AppData\Roaming\Microsoft, etc.)
///   • Recursive operations that would affect more than <see cref="MaxFilesPerOperation"/> files
///   • Operations on paths outside the user's profile unless explicitly scoped
///
/// Path rules are shared with the execution layer via
/// <see cref="ExecutionPathGuard"/> so autonomy workflows and direct
/// executors enforce identical protections.
///
/// All methods are synchronous and pure — no I/O, no state mutations.
/// Thread-safe: all members are static.
/// </summary>
public sealed class SafeExecutionValidator
{
    // ── Limits ────────────────────────────────────────────────────────────────

    /// <summary>Maximum files that can be touched in a single workflow execution.</summary>
    public const int MaxFilesPerOperation = 500;

    /// <summary>Maximum total bytes that can be moved/deleted in one workflow execution (10 GB).</summary>
    public const long MaxBytesPerOperation = 10L * 1024 * 1024 * 1024;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates that the given target path is safe to operate on.
    /// Returns a block reason string if blocked, or null if safe.
    /// Delegates to <see cref="ExecutionPathGuard.CheckDestructiveTarget"/> —
    /// the single denylist shared with the execution layer.
    /// </summary>
    public string? ValidatePath(string path) =>
        ExecutionPathGuard.CheckDestructiveTarget(path);

    /// <summary>
    /// Validates a <see cref="FileOrganizationPreview"/> before execution.
    /// Returns a block reason if any proposal violates safety rules, or null if valid.
    /// </summary>
    public string? ValidatePreview(FileOrganizationPreview preview)
    {
        if (preview.TotalFiles > MaxFilesPerOperation)
            return $"This operation would affect {preview.TotalFiles} files, which exceeds " +
                   $"the safe limit of {MaxFilesPerOperation}. Please narrow the scope.";

        if (preview.TotalBytes > MaxBytesPerOperation)
            return $"This operation would touch {FormatBytes(preview.TotalBytes)} of data, " +
                   "which exceeds the safe limit of 10 GB. Please narrow the scope.";

        foreach (var proposal in preview.Proposals)
        {
            var sourceCheck = ValidatePath(proposal.SourcePath);
            if (sourceCheck is not null) return sourceCheck;

            if (!proposal.IsRecycleDeletion)
            {
                var destCheck = ValidatePath(proposal.DestinationPath);
                if (destCheck is not null) return destCheck;
            }
        }

        return null;  // valid
    }

    /// <summary>
    /// Validates a <see cref="WorkflowStep"/> before execution.
    /// Returns a block reason if blocked, or null if safe.
    /// </summary>
    public string? ValidateStep(WorkflowStep step)
    {
        // Validate target path if present
        if (!string.IsNullOrWhiteSpace(step.TargetPath))
        {
            var pathCheck = ValidatePath(step.TargetPath);
            if (pathCheck is not null) return pathCheck;
        }

        // Steps that require approval must have passed the review gate
        // (enforced by HumanReviewGate — validator just confirms the step type)
        if (step.StepType == WorkflowStepType.FileOperation && step.Risk >= Trust.TrustRiskLevel.High)
        {
            // High-risk file operations need approval — checked by HumanReviewGate
            // This is a secondary defensive check
            if (!step.RequiresApproval)
                return "High-risk file operations must require approval. " +
                       "This is a workflow planning error.";
        }

        return null;  // valid
    }

    /// <summary>
    /// Returns true if the given path appears to be a user-writable location
    /// that Lucid can reasonably offer to organise.
    /// </summary>
    public bool IsUserWritablePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                                     .ToLowerInvariant();
        var lower = path.ToLowerInvariant();

        // Must be under the user's profile
        if (!lower.StartsWith(userProfile, StringComparison.Ordinal)) return false;

        // Must not be a blocked path
        return ValidatePath(path) is null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatBytes(long b) => b switch
    {
        >= 1_073_741_824 => $"{b / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{b / 1_048_576.0:F1} MB",
        _                => $"{b / 1_024.0:F0} KB",
    };
}
