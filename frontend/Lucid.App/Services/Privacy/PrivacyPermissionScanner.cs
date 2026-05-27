using Microsoft.Win32;

namespace Lucid.Services.Privacy;

/// <summary>
/// Reads Windows privacy permission data from the CapabilityAccessManager ConsentStore.
///
/// Registry path scanned (current user):
///   HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore
///
/// For each capability subkey (webcam, microphone, location, …):
///   • The key's own "Value" REG_SZ gives the global on/off state ("Allow" / "Deny").
///   • Each child subkey represents one app that has requested the permission.
///     - Packaged apps:  key name is the Package Family Name (e.g. "Microsoft.WindowsCamera_8wekyb3d8bbwe")
///     - Desktop apps:   key name starts with "NonPackaged\" followed by the exe path
///                       with backslashes replaced by "#"
///     - "Value" REG_SZ in the child key = "Allow" or "Deny"
///     - "LastUsedTimeStop" REG_QWORD = FILETIME of last use (0 = never used)
///
/// Only categories that contain at least one app entry are returned.
///
/// Threading:
///   All operations are synchronous. Wrap in Task.Run when calling from the UI thread.
/// </summary>
public static class PrivacyPermissionScanner
{
    private const string ConsentStorePath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    // Human-readable metadata for the well-known capability keys.
    private static readonly Dictionary<string, (string DisplayName, string Glyph)> s_knownCapabilities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["webcam"]                      = ("Camera",             ""),
            ["microphone"]                  = ("Microphone",         ""),
            ["location"]                    = ("Location",           ""),
            ["userAccountInformation"]      = ("Account Info",       ""),
            ["calendar"]                    = ("Calendar",           ""),
            ["contacts"]                    = ("Contacts",           ""),
            ["email"]                       = ("Email",              ""),
            ["appDiagnostics"]              = ("App Diagnostics",    ""),
            ["userNotificationListener"]    = ("Notifications",      ""),
            ["documentsLibrary"]            = ("Documents",          ""),
            ["picturesLibrary"]             = ("Pictures",           ""),
            ["videosLibrary"]               = ("Videos",             ""),
            ["musicLibrary"]                = ("Music",              ""),
            ["broadFileSystemAccess"]       = ("File System",        ""),
            ["phoneCall"]                   = ("Phone Calls",        ""),
            ["chat"]                        = ("Chat / SMS",         ""),
            ["backgroundSpatialPerception"] = ("Spatial Perception", ""),
            ["phoneCallHistory"]            = ("Call History",       ""),
        };

    /// <summary>
    /// Scans the HKCU ConsentStore and returns privacy categories that have at least
    /// one app entry, sorted by app count (most-requested permissions first).
    ///
    /// Falls back to HKLM if the HKCU key is absent or inaccessible.
    /// </summary>
    public static IReadOnlyList<PrivacyCategoryRecord> Scan()
    {
        var results = new List<PrivacyCategoryRecord>();

        RegistryKey? store = null;
        try
        {
            store = Registry.CurrentUser.OpenSubKey(ConsentStorePath, writable: false)
                 ?? Registry.LocalMachine.OpenSubKey(ConsentStorePath, writable: false);

            if (store is null) return results;

            foreach (var capName in store.GetSubKeyNames())
            {
                try
                {
                    using var capKey = store.OpenSubKey(capName, writable: false);
                    if (capKey is null) continue;

                    var category = ScanCapability(capKey, capName);
                    if (category.Apps.Count > 0)
                        results.Add(category);
                }
                catch { /* best-effort — skip individual capability keys */ }
            }
        }
        catch { /* ConsentStore inaccessible — return empty list */ }
        finally
        {
            store?.Dispose();
        }

        // Surface permissions with the most app entries first — they represent
        // the highest exposure surface and are the most actionable.
        return results
            .OrderByDescending(c => c.Apps.Count)
            .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static PrivacyCategoryRecord ScanCapability(RegistryKey capKey, string capName)
    {
        // Global enabled state for this capability (the master system toggle).
        var globalValue     = capKey.GetValue("Value") as string ?? "Allow";
        var globallyEnabled = !string.Equals(globalValue, "Deny", StringComparison.OrdinalIgnoreCase);

        s_knownCapabilities.TryGetValue(capName, out var meta);
        var displayName = string.IsNullOrEmpty(meta.DisplayName) ? FormatCapabilityName(capName) : meta.DisplayName;
        var glyph       = string.IsNullOrEmpty(meta.Glyph)       ? ""                      : meta.Glyph;

        var apps = new List<PrivacyPermissionRecord>();

        foreach (var appKeyName in capKey.GetSubKeyNames())
        {
            try
            {
                using var appKey = capKey.OpenSubKey(appKeyName, writable: false);
                if (appKey is null) continue;

                var appValue  = appKey.GetValue("Value") as string ?? "Allow";
                var isAllowed = !string.Equals(appValue, "Deny", StringComparison.OrdinalIgnoreCase);

                // LastUsedTimeStop is a REG_QWORD FILETIME (100-ns intervals since 1601-01-01 UTC).
                // A value of zero means the permission was granted but never actually exercised.
                DateTimeOffset? lastUsed = null;
                if (appKey.GetValue("LastUsedTimeStop") is long ft && ft > 0)
                {
                    try { lastUsed = DateTimeOffset.FromFileTime(ft); }
                    catch { /* invalid FILETIME — ignore */ }
                }

                var isPackaged   = !appKeyName.StartsWith("NonPackaged", StringComparison.OrdinalIgnoreCase);
                var appName      = ExtractDisplayName(appKeyName, isPackaged);

                apps.Add(new PrivacyPermissionRecord
                {
                    PermissionType = capName,
                    AppDisplayName = appName,
                    AppIdentifier  = appKeyName,
                    IsAllowed      = isAllowed,
                    IsPackaged     = isPackaged,
                    LastUsed       = lastUsed,
                });
            }
            catch { /* skip individual malformed app entries */ }
        }

        // Allowed entries first (user cares most about what has access),
        // then alphabetically within each group.
        apps.Sort((a, b) =>
        {
            var byAllowed = b.IsAllowed.CompareTo(a.IsAllowed);
            return byAllowed != 0
                ? byAllowed
                : string.Compare(a.AppDisplayName, b.AppDisplayName, StringComparison.OrdinalIgnoreCase);
        });

        return new PrivacyCategoryRecord
        {
            PermissionType  = capName,
            DisplayName     = displayName,
            Glyph           = glyph,
            GloballyEnabled = globallyEnabled,
            Apps            = apps,
        };
    }

    /// <summary>
    /// Derives a short, readable app name from the registry key name.
    ///
    /// Packaged:  "Microsoft.Windows.Photos_8wekyb3d8bbwe"
    ///            → strip publisher hash → "Microsoft.Windows.Photos"
    ///            → take last dot-segment  → "Photos"
    ///
    /// Non-packaged: "NonPackaged\C:#Users#tyler#AppData#Local#Zoom#bin#Zoom.exe"
    ///            → strip prefix, replace # with \, extract filename → "Zoom"
    /// </summary>
    private static string ExtractDisplayName(string appKeyName, bool isPackaged)
    {
        if (isPackaged)
        {
            // "Microsoft.Windows.Photos_8wekyb3d8bbwe" → "Photos"
            var underscoreIdx = appKeyName.LastIndexOf('_');
            var packageName   = underscoreIdx > 0 ? appKeyName[..underscoreIdx] : appKeyName;
            var dotIdx        = packageName.LastIndexOf('.');
            return dotIdx >= 0 && dotIdx < packageName.Length - 1
                ? packageName[(dotIdx + 1)..]
                : packageName;
        }
        else
        {
            // "NonPackaged\C:#Windows#System32#svchost.exe" → "svchost"
            var path = appKeyName;
            if (path.StartsWith("NonPackaged\\", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("NonPackaged/",  StringComparison.OrdinalIgnoreCase))
                path = path[12..]; // strip prefix

            // Replace '#' separators with backslashes to reconstruct the path.
            path = path.Replace('#', '\\');

            // Extract file name without extension — handles both paths and bare names.
            if (path.Contains('\\') || path.Contains('/'))
            {
                try { return Path.GetFileNameWithoutExtension(path); }
                catch { /* fall through */ }
            }

            // Truncate long raw strings that couldn't be parsed as paths.
            return path.Length > 48 ? path[..48] + "…" : path;
        }
    }

    /// <summary>
    /// Converts a camelCase capability name to a space-separated title.
    /// "userAccountInformation" → "User Account Information"
    /// </summary>
    private static string FormatCapabilityName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        var sb = new System.Text.StringBuilder();
        sb.Append(char.ToUpperInvariant(raw[0]));
        for (var i = 1; i < raw.Length; i++)
        {
            if (char.IsUpper(raw[i]) && !char.IsUpper(raw[i - 1]))
                sb.Append(' ');
            sb.Append(raw[i]);
        }
        return sb.ToString();
    }
}
