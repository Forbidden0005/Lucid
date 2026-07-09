using System.Text.RegularExpressions;

namespace Lucid.Services.ProcessIntel;

/// <summary>
/// Local, explainable heuristics for spotting out-of-place process behavior —
/// masquerading, suspicious parent/child chains, living-off-the-land binary
/// (LOLBin) abuse patterns, and command-line obfuscation.
///
/// Design principles (matches <see cref="Lucid.Services.Security.PersistenceScanner"/>):
///   • Every check here is a single weak-to-moderate contextual signal, not a verdict.
///   • No signal alone claims maliciousness — callers surface these as anomaly
///     flags with confidence-aware language, same as the rest of process intelligence.
///   • All checks are local string/path comparisons — no network, no cloud lookups.
/// </summary>
internal static class ProcessSecurityHeuristics
{
    // ── Masquerading (MITRE T1036.005 — match legitimate resource name/location) ──

    // Well-known Windows system binaries and the directories they legitimately
    // run from. A process using one of these names anywhere else is a classic
    // masquerading pattern (e.g. a lookalike "svchost.exe" launched from Temp).
    private static readonly Dictionary<string, string[]> s_expectedSystemDirs =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["svchost"]       = new[] { @"\windows\system32\", @"\windows\syswow64\" },
        ["csrss"]         = new[] { @"\windows\system32\" },
        ["wininit"]       = new[] { @"\windows\system32\" },
        ["winlogon"]      = new[] { @"\windows\system32\" },
        ["services"]      = new[] { @"\windows\system32\" },
        ["lsass"]         = new[] { @"\windows\system32\" },
        ["smss"]          = new[] { @"\windows\system32\" },
        ["dwm"]           = new[] { @"\windows\system32\" },
        ["spoolsv"]       = new[] { @"\windows\system32\" },
        ["taskhostw"]     = new[] { @"\windows\system32\" },
        ["conhost"]       = new[] { @"\windows\system32\" },
        ["searchindexer"] = new[] { @"\windows\system32\" },
        ["explorer"]      = new[] { @"\windows\" },
    };

    /// <summary>
    /// True if a well-known system process name is running from somewhere other
    /// than its normal Windows directory. Single-signal but high-precision —
    /// these exact names have essentially no legitimate reason to run elsewhere.
    /// </summary>
    internal static bool IsMasquerading(string processName, string execPath)
    {
        if (string.IsNullOrEmpty(execPath)) return false;

        var key = Path.GetFileNameWithoutExtension(processName);
        if (!s_expectedSystemDirs.TryGetValue(key, out var dirs)) return false;

        var lower = execPath.ToLowerInvariant();
        return !dirs.Any(d => lower.Contains(d));
    }

    // ── Suspicious parent/child chains (MITRE T1218 lineage) ─────────────────────

    // Apps that consume documents or browse the web don't normally spawn a
    // scripting engine or system utility during ordinary use. This chain is the
    // textbook macro/exploit execution pattern.
    private static readonly HashSet<string> s_shellSpawnParents = new(StringComparer.OrdinalIgnoreCase)
    {
        "winword", "excel", "powerpnt", "outlook", "onenote", "mspub", "msaccess",
        "chrome", "msedge", "firefox", "brave", "iexplore",
    };

    private static readonly HashSet<string> s_lolbins = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "powershell", "pwsh", "wscript", "cscript", "mshta",
        "rundll32", "regsvr32", "certutil", "bitsadmin", "wmic",
        "msbuild", "installutil", "regasm", "csc",
    };

    /// <summary>
    /// True if a document/browser process spawned a scripting or system-utility
    /// binary — a well-established initial-execution chain, not a conclusive
    /// finding on its own (some legitimate add-ins do this too).
    /// </summary>
    internal static bool IsSuspiciousParentChild(string parentName, string childName)
    {
        if (string.IsNullOrEmpty(parentName) || string.IsNullOrEmpty(childName)) return false;

        var parent = Path.GetFileNameWithoutExtension(parentName);
        var child  = Path.GetFileNameWithoutExtension(childName);
        return s_shellSpawnParents.Contains(parent) && s_lolbins.Contains(child);
    }

    internal static bool IsLolbin(string processName) =>
        s_lolbins.Contains(Path.GetFileNameWithoutExtension(processName));

    // ── Command-line obfuscation heuristics ───────────────────────────────────────

    private static readonly Regex s_base64Blob =
        new(@"[A-Za-z0-9+/]{40,}={0,2}", RegexOptions.Compiled);

    private static readonly string[] s_suspiciousFragments =
    [
        "-enc", "-encodedcommand", "frombase64string",
        "downloadstring", "downloadfile", "-w hidden", "-windowstyle hidden",
        "bypass", "invoke-expression", "iex ",
    ];

    /// <summary>
    /// Flags command-line patterns commonly used to obfuscate or hide execution —
    /// encoded PowerShell payloads, hidden windows, execution-policy bypass, long
    /// base64-looking blobs. Heuristic only: legitimate automation and deployment
    /// scripts can trigger this, so it is only meaningful combined with a LOLBin
    /// process name or an unusual parent.
    /// </summary>
    internal static bool HasSuspiciousCommandLine(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return false;
        var lower = commandLine.ToLowerInvariant();

        if (s_suspiciousFragments.Any(lower.Contains)) return true;
        return s_base64Blob.IsMatch(commandLine);
    }

    // ── Unsigned binaries in system paths ─────────────────────────────────────────

    /// <summary>
    /// True when an unsigned executable is running from a Windows system
    /// directory. Genuine Windows system binaries are always Authenticode-signed
    /// by Microsoft, so an unsigned file there is unusual regardless of its name.
    /// </summary>
    internal static bool IsUnsignedInSystemPath(string execPath, bool isSigned)
    {
        if (isSigned || string.IsNullOrEmpty(execPath)) return false;
        var lower = execPath.ToLowerInvariant();
        return lower.Contains(@"\windows\system32\") || lower.Contains(@"\windows\syswow64\");
    }
}
