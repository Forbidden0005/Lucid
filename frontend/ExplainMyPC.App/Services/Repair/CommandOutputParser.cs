using System.Text.RegularExpressions;

namespace ExplainMyPC.Services.Repair;

/// <summary>
/// Parsing helpers for system tool output lines.
///
/// Each method is pure (no side effects, no log writes) so it can be
/// used inside <see cref="ProcessExecutionHelper.Run"/>'s
/// <c>progressLineCallback</c> without threading concerns.
/// </summary>
internal static class CommandOutputParser
{
    // ── SFC /scannow ──────────────────────────────────────────────────────────

    // "Verification 45 percent complete."
    // "Verification 100 percent complete."
    private static readonly Regex s_sfcPercent =
        new(@"Verification\s+(\d+)\s+percent", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Extracts the completion percentage from an SFC output line.
    /// Returns <see langword="true"/> and sets <paramref name="percent"/> (0–100)
    /// when the line is a progress line; otherwise returns false.
    /// </summary>
    internal static bool TryParseSfcProgress(string line, out int percent)
    {
        percent = 0;
        var m = s_sfcPercent.Match(line);
        if (!m.Success) return false;
        return int.TryParse(m.Groups[1].Value, out percent);
    }

    /// <summary>
    /// Classifies an SFC result from the lines it produced.
    /// Scans the accumulated log output for the well-known SFC outcome sentences.
    /// </summary>
    internal static SfcResult ClassifySfcResult(int exitCode, IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            // Happy path — no violations found.
            if (line.Contains("did not find any integrity violations", StringComparison.OrdinalIgnoreCase))
                return SfcResult.NoViolationsFound;

            // Repairs succeeded.
            if (line.Contains("found corrupt files and successfully repaired", StringComparison.OrdinalIgnoreCase))
                return SfcResult.RepairedSuccessfully;

            // Found problems but could not fix them.
            if (line.Contains("found corrupt files but was unable to fix", StringComparison.OrdinalIgnoreCase))
                return SfcResult.UnrepairedCorruptionFound;

            // Could not scan (pending restart from a previous SFC run, etc.)
            if (line.Contains("could not perform the requested operation", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("pending repair operations", StringComparison.OrdinalIgnoreCase))
                return SfcResult.PendingRestartRequired;
        }

        return exitCode == 0 ? SfcResult.NoViolationsFound : SfcResult.UnknownError;
    }

    // ── DISM /Cleanup-Image ───────────────────────────────────────────────────

    // DISM progress: "[===========        ] 58.6%" — percentage after the bracket.
    // The bracket portion varies; we just extract the trailing number.
    private static readonly Regex s_dismPercent =
        new(@"\]\s*([\d.]+)\s*%", RegexOptions.Compiled);

    /// <summary>
    /// Extracts the completion percentage from a DISM output line.
    /// Returns <see langword="true"/> and sets <paramref name="percent"/> (0.0–100.0)
    /// when the line is a DISM progress indicator.
    /// </summary>
    internal static bool TryParseDismProgress(string line, out float percent)
    {
        percent = 0f;
        var m = s_dismPercent.Match(line);
        if (!m.Success) return false;
        return float.TryParse(
            m.Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out percent);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the line is a DISM progress bar
    /// that should be suppressed from the log (we log sampled progress instead).
    /// </summary>
    internal static bool IsDismProgressLine(string line) =>
        line.TrimStart().StartsWith('[') && line.Contains('%');

    /// <summary>
    /// Classifies a DISM result from exit code and output lines.
    /// </summary>
    internal static DismResult ClassifyDismResult(int exitCode, IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            if (line.Contains("The restore operation completed successfully", StringComparison.OrdinalIgnoreCase))
                return DismResult.RestoredSuccessfully;

            if (line.Contains("The component store is repairable", StringComparison.OrdinalIgnoreCase))
                return DismResult.RestoredSuccessfully;

            if (line.Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase))
                return DismResult.NoCorruptionFound;

            if (line.Contains("The source files could not be found", StringComparison.OrdinalIgnoreCase))
                return DismResult.SourceNotFound;

            if (line.Contains("The component store cannot be repaired", StringComparison.OrdinalIgnoreCase))
                return DismResult.CannotRepair;

            if (line.Contains("A restart is needed", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Please restart Windows", StringComparison.OrdinalIgnoreCase))
                return DismResult.RestartRequired;
        }

        return exitCode == 0 ? DismResult.RestoredSuccessfully : DismResult.UnknownError;
    }

    // ── ipconfig /flushdns ────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when the ipconfig /flushdns output
    /// confirms the cache was cleared.
    /// </summary>
    internal static bool FlushDnsSucceeded(IReadOnlyList<string> lines) =>
        lines.Any(l => l.Contains("Successfully flushed", StringComparison.OrdinalIgnoreCase));

    // ── netsh winsock reset ───────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when the winsock reset output confirms
    /// that the catalog was reset (restart still required before it takes effect).
    /// </summary>
    internal static bool WinsockResetSucceeded(IReadOnlyList<string> lines) =>
        lines.Any(l =>
            l.Contains("Successfully reset", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("Winsock reset completed", StringComparison.OrdinalIgnoreCase));

    // ── Line interest filter ──────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="false"/> for decorative separator lines that
    /// tool outputs often include (lines of dashes, equals signs, etc.)
    /// to keep the log readable.
    /// </summary>
    internal static bool IsInterestingLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        var trimmed = line.Trim();
        // Lines that are purely punctuation/decoration
        if (trimmed.All(c => c is '-' or '=' or '*' or '_' or '.' or ' '))
            return false;
        return true;
    }
}

// ── Result enums ──────────────────────────────────────────────────────────────

internal enum SfcResult
{
    NoViolationsFound,
    RepairedSuccessfully,
    UnrepairedCorruptionFound,
    PendingRestartRequired,
    UnknownError,
}

internal enum DismResult
{
    RestoredSuccessfully,
    NoCorruptionFound,
    SourceNotFound,
    CannotRepair,
    RestartRequired,
    UnknownError,
}
