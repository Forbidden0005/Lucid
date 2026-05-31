using System.Diagnostics;

namespace ExplainMyPC.Services.Execution.Validation;

/// <summary>
/// Verifies the OS-level identity of a process by PID before any
/// destructive action (termination, suspension, etc.) is taken.
///
/// Security rationale:
///   Windows PIDs are recycled. A PID supplied by the caller at insight
///   generation time may belong to a different process by execution time.
///   This validator asks the OS for the actual process name and compares
///   it against the caller's expectation — aborting if they differ.
///
/// Usage:
///   Call <see cref="TryVerify"/> before any action that targets a PID.
///   Abort if the result is null (process gone) or
///   <see cref="VerifiedProcessTarget.IsConfirmed"/> is false (PID recycled).
/// </summary>
public static class ProcessIdentityValidator
{
    /// <summary>
    /// Attempts to verify the identity of the process with the given
    /// <paramref name="pid"/> against <paramref name="expectedName"/>.
    ///
    /// Returns null if the process no longer exists (already exited or access denied).
    /// Returns a <see cref="VerifiedProcessTarget"/> otherwise — check
    /// <see cref="VerifiedProcessTarget.IsConfirmed"/> before proceeding.
    /// </summary>
    /// <param name="pid">OS process identifier.</param>
    /// <param name="expectedName">
    ///   Process name supplied by the caller (e.g. "chrome", "chrome.exe").
    ///   The comparison strips the .exe extension if present.
    ///   Pass null to skip the name check (IsConfirmed will be true if alive).
    /// </param>
    public static VerifiedProcessTarget? TryVerify(int pid, string? expectedName)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);

            var actualName = proc.ProcessName; // Windows returns name without .exe

            string mainModulePath = string.Empty;
            try
            {
                // May throw for system processes or when running without elevation.
                mainModulePath = proc.MainModule?.FileName ?? string.Empty;
            }
            catch (Exception)
            {
                // Best-effort — the path is for diagnostics only, not the safety check.
            }

            bool matches = expectedName is null || NamesMatch(actualName, expectedName);

            return new VerifiedProcessTarget(
                ProcessId:           pid,
                VerifiedName:        actualName,
                MainModulePath:      mainModulePath,
                MatchesExpectedName: matches);
        }
        catch (ArgumentException)
        {
            // Process.GetProcessById throws ArgumentException when the PID does not exist.
            return null;
        }
        catch (InvalidOperationException)
        {
            // Process has exited between the GetProcessById call and accessing properties.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // The process exists but we cannot query its identity (e.g. a highly-privileged
            // system process running without elevation). Return a VerifiedProcessTarget with
            // an empty name and IsConfirmed=false so callers can distinguish this from
            // "process already gone" (null) and abort rather than silently succeed.
            return new VerifiedProcessTarget(
                ProcessId:           pid,
                VerifiedName:        string.Empty,
                MainModulePath:      string.Empty,
                MatchesExpectedName: false);
        }
        catch (Exception)
        {
            // Other unexpected exceptions — treat conservatively as unverifiable.
            return new VerifiedProcessTarget(
                ProcessId:           pid,
                VerifiedName:        string.Empty,
                MainModulePath:      string.Empty,
                MatchesExpectedName: false);
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Compares two process names, normalising .exe extensions and case.
    /// "chrome.exe" == "chrome" == "Chrome".
    /// </summary>
    private static bool NamesMatch(string actual, string expected)
    {
        var a = StripExe(actual);
        var e = StripExe(expected);
        return string.Equals(a, e, StringComparison.OrdinalIgnoreCase);
    }

    private static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
}
