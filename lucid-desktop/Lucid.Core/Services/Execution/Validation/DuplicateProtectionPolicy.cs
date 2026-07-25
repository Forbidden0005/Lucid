namespace Lucid.Services.Execution.Validation;

/// <summary>
/// Single source of truth for deciding whether a duplicate-file group may be
/// cleaned up, shared by <c>DeleteDuplicateGroupExecutor</c> (which enforces it
/// before staging deletes) and the Storage UI (which uses it to route groups
/// into the actionable list vs. the "review manually" section).
///
/// A group is protected when the keep candidate OR any delete candidate lives
/// in a location <see cref="ExecutionPathGuard"/> guards: the keep file is never
/// deleted, but a protected keep path means the grouping itself can't be
/// trusted, so the whole group is refused. Keeping this in one place ensures the
/// UI never shows a delete button the executor is guaranteed to reject.
///
/// Pure and static — no I/O, no state.
/// </summary>
public static class DuplicateProtectionPolicy
{
    /// <summary>
    /// Returns the user-facing block reason for the first protected path in the
    /// group, or null when every path is safe to operate on. An absent keep
    /// path is not an error — only the delete list participates then.
    /// </summary>
    public static string? FindBlockedReason(string? keepPath, IEnumerable<string> deletePaths)
    {
        if (!string.IsNullOrWhiteSpace(keepPath) &&
            ExecutionPathGuard.CheckDestructiveTarget(keepPath) is { } keepReason)
            return keepReason;

        foreach (var path in deletePaths)
        {
            if (ExecutionPathGuard.CheckDestructiveTarget(path) is { } reason)
                return reason;
        }

        return null;
    }

    /// <summary>True when the group touches a protected location.</summary>
    public static bool IsProtected(string? keepPath, IEnumerable<string> deletePaths)
        => FindBlockedReason(keepPath, deletePaths) is not null;
}
