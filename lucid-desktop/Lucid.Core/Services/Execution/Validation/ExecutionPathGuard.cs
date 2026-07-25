namespace Lucid.Services.Execution.Validation;

/// <summary>
/// Shared path-safety guard for every executor that deletes, moves, or
/// otherwise mutates a caller-supplied filesystem path.
///
/// Security rationale:
///   Executor parameters arrive as plain strings from ViewModels, insight
///   actions, and automation flows. A mis-attributed scanner result or a
///   malformed action parameter must never be able to point a destructive
///   executor at a Windows system directory, driver store, or credential
///   store. This guard is the single denylist for those locations —
///   <see cref="Lucid.Services.Autonomy.SafeExecutionValidator"/> delegates
///   to it so autonomy workflows and direct executors enforce identical rules.
///
/// Usage:
///   Call <see cref="CheckDestructiveTarget"/> before staging or deleting.
///   A non-null return is the user-facing block reason; the executor must
///   fail without mutating anything. Dry-run paths must call it too, so the
///   preview the user approves already surfaces the refusal.
///
/// All members are static and pure — no I/O, no state.
/// </summary>
public static class ExecutionPathGuard
{
    // ── Blocked path fragments ────────────────────────────────────────────────
    // Matched case-insensitively against the normalised ('\'-separated,
    // lower-cased) full path. Keep in sync with the doctrine: conservative,
    // explainable — when in doubt, block and let the user act manually.

    private static readonly string[] _blockedPathFragments =
    [
        // Windows system
        @"\windows\",
        @"\system32\",
        @"\syswow64\",
        @"\program files\",
        @"\program files (x86)\",
        @"\programdata\",
        @"\windows.old\",
        @"\boot\",
        @"\efi\",
        @"\recovery\",

        // Driver / firmware
        @"\drivers\",
        @"\inf\",
        @"\diagerr.xml",

        // User credential / browser stores
        @"\appdata\roaming\microsoft\credentials\",
        @"\appdata\roaming\microsoft\protect\",
        @"\appdata\local\microsoft\credentials\",
        @"\appdata\local\microsoft\vault\",
        @"\appdata\roaming\microsoft\wallet\",

        // Browser profile data
        @"\appdata\local\google\chrome\user data\default\login data",
        @"\appdata\roaming\mozilla\firefox\profiles\",
        @"\appdata\local\microsoft\edge\user data\default\login data",
        @"\cookies\",
        @"\login data",

        // Hidden system data
        @"\appdata\roaming\microsoft\windows\",
        @"\appdata\local\packages\",
        @"\appdata\local\temp\wer\",
    ];

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates that <paramref name="path"/> is safe for a destructive
    /// operation (delete / move / overwrite).
    /// Returns a user-facing block reason if the path is protected,
    /// or null if the operation may proceed.
    /// </summary>
    public static string? CheckDestructiveTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Path is empty or null.";

        var lower = path.ToLowerInvariant().Replace('/', '\\');

        foreach (var fragment in _blockedPathFragments)
        {
            if (lower.Contains(fragment, StringComparison.Ordinal))
                return $"Operation not permitted: the path '{Path.GetFileName(path)}' is in a " +
                       "protected system directory. Lucid cannot modify this location.";
        }

        // Block operations on drive roots
        if (lower.Length <= 3 && lower.Length >= 2 && lower[1] == ':')
            return "Operations on drive roots are not permitted.";

        return null;  // safe
    }
}
