using Lucid.Services.Startup;
using Microsoft.Win32;

namespace Lucid.Services.Apps;

/// <summary>
/// Reads installed applications from the Windows Uninstall registry keys and
/// joins them with a provided startup entry list.
///
/// Registry paths scanned (in order, deduplicated by display name):
///   HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall        — 64-bit apps
///   HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall — 32-bit apps
///   HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall        — per-user installs
///
/// Filtering:
///   • SystemComponent = 1   → skipped (OS infrastructure, not user-visible apps)
///   • ParentKeyName set     → skipped (sub-components of a parent installer bundle)
///   • DisplayName absent    → skipped
///
/// Threading:
///   All operations are synchronous and run on the calling thread.
///   Wrap in Task.Run when calling from the UI thread.
/// </summary>
public static class InstalledAppScanner
{
    private static readonly string[] HklmPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    /// <summary>
    /// Scans all Uninstall registry hives and returns a deduplicated list of
    /// installed apps, each joined with its startup entry (when applicable).
    ///
    /// Sort order: apps with a startup entry first (High → Moderate → Low → Unknown),
    /// then the remaining apps alphabetically.
    /// </summary>
    public static IReadOnlyList<InstalledAppRecord> Scan(
        IReadOnlyList<StartupEntry> startupEntries)
    {
        var startupLookup = BuildStartupLookup(startupEntries);

        // Deduplicate across all three hives — first-seen display name wins.
        var seen = new Dictionary<string, InstalledAppRecord>(
            StringComparer.OrdinalIgnoreCase);

        // HKLM — 64-bit and 32-bit WOW node
        foreach (var path in HklmPaths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path, writable: false);
                if (key is not null) ScanHive(key, seen, startupLookup);
            }
            catch { /* best-effort — HKLM may be restricted */ }
        }

        // HKCU — per-user installs (e.g. apps installed without elevation)
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", writable: false);
            if (key is not null) ScanHive(key, seen, startupLookup);
        }
        catch { /* best-effort */ }

        return seen.Values
            .OrderByDescending(a => a.LinkedStartupEntry is not null)
            .ThenByDescending(a => (int)(a.LinkedStartupEntry?.Impact ?? StartupImpact.Unknown))
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static void ScanHive(
        RegistryKey                            hive,
        Dictionary<string, InstalledAppRecord> seen,
        Dictionary<string, StartupEntry>       startupLookup)
    {
        foreach (var subKeyName in hive.GetSubKeyNames())
        {
            try
            {
                using var sub = hive.OpenSubKey(subKeyName, writable: false);
                if (sub is null) continue;

                // Skip OS components and bundled sub-installers.
                if (sub.GetValue("SystemComponent") is int sc && sc == 1) continue;
                if (sub.GetValue("ParentKeyName") is string parent
                    && !string.IsNullOrEmpty(parent)) continue;

                var name = sub.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(name)) continue;

                // First-seen wins across hives.
                if (seen.ContainsKey(name)) continue;

                var publisher   = (sub.GetValue("Publisher")       as string) ?? "";
                var version     = (sub.GetValue("DisplayVersion")   as string) ?? "";
                var sizeKb      = sub.GetValue("EstimatedSize") is int kb ? (long)kb : 0L;
                var installDate = TryParseInstallDate(sub.GetValue("InstallDate") as string);
                var startup     = FindStartupEntry(name, startupLookup);

                seen[name] = new InstalledAppRecord
                {
                    Name               = name,
                    Publisher          = publisher,
                    Version            = version,
                    InstallDate        = installDate,
                    EstimatedSizeKb    = sizeKb,
                    LinkedStartupEntry = startup,
                };
            }
            catch { /* skip individual malformed entries */ }
        }
    }

    /// <summary>
    /// Builds a lookup table from startup entries keyed by both entry name and
    /// executable name (lower-case) so matching works for either.
    /// </summary>
    private static Dictionary<string, StartupEntry> BuildStartupLookup(
        IReadOnlyList<StartupEntry> entries)
    {
        var lookup = new Dictionary<string, StartupEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            lookup.TryAdd(e.Name, e);
            if (!string.IsNullOrEmpty(e.ExecutableName))
                lookup.TryAdd(e.ExecutableName, e);
        }
        return lookup;
    }

    /// <summary>
    /// Attempts to match an app display name to a startup entry via three strategies:
    ///   1. Exact match on startup entry name or executable name (case-insensitive).
    ///   2. The startup entry name appears as a substring of the app display name.
    ///   3. The app display name appears as a substring of the startup entry name.
    ///
    /// Short keys (≤ 3 chars) are skipped in substring matching to avoid false positives.
    /// Returns the first match found, or null.
    /// </summary>
    private static StartupEntry? FindStartupEntry(
        string                           appName,
        Dictionary<string, StartupEntry> lookup)
    {
        // Strategy 1: exact / exe-name lookup
        if (lookup.TryGetValue(appName, out var exact)) return exact;

        // Strategy 2 & 3: substring matching
        var lowerApp = appName.ToLowerInvariant();
        foreach (var (key, entry) in lookup)
        {
            if (key.Length <= 3) continue;
            var lowerKey = key.ToLowerInvariant();

            if (lowerApp.Contains(lowerKey, StringComparison.Ordinal) ||
                lowerKey.Contains(lowerApp, StringComparison.Ordinal))
                return entry;
        }

        return null;
    }

    /// <summary>
    /// Parses the YYYYMMDD date string stored in the InstallDate registry value.
    /// Returns null when the string is absent, malformed, or represents an invalid date.
    /// </summary>
    private static DateOnly? TryParseInstallDate(string? raw)
    {
        if (raw is null || raw.Length != 8) return null;
        if (int.TryParse(raw[..4], out var y) &&
            int.TryParse(raw[4..6], out var m) &&
            int.TryParse(raw[6..8], out var d))
        {
            try { return new DateOnly(y, m, d); }
            catch { /* invalid calendar date */ }
        }
        return null;
    }
}
