using Microsoft.Win32;

namespace ExplainMyPC.Services.Startup;

/// <summary>
/// Enumerates Windows startup applications from two sources:
///   1. Registry Run keys — HKCU and HKLM SOFTWARE\...\CurrentVersion\Run
///   2. Per-user Startup folder — %APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup
///
/// Each entry is classified by <see cref="StartupImpact"/> using a static lookup
/// table of known-app exe names. Apps not in the table are classified as Unknown.
///
/// The sampler is stateless between calls — it rebuilds the list from scratch
/// on every <see cref="Sample"/> call. The caller (WindowsTelemetryService) is
/// responsible for throttling how often Sample is called.
///
/// Error policy:
///   Registry access and folder enumeration are individually try/caught.
///   A single key or file failing to open does not prevent the rest from loading.
///   On totally locked-down systems the result may be an empty list.
/// </summary>
public sealed class StartupSampler
{
    // ── Known-app impact catalogue ────────────────────────────────────────────
    //
    // Keys are lower-case exe names without the .exe extension.
    // Unlisted apps fall back to StartupImpact.Unknown.

    private static readonly Dictionary<string, StartupImpact> s_impactMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // ── Browsers ──────────────────────────────────────────────────────
            // Browsers load large runtimes, start background services, and often
            // schedule update checks — all on sign-in.
            { "chrome",              StartupImpact.High },
            { "msedge",              StartupImpact.High },
            { "firefox",             StartupImpact.High },
            { "opera",               StartupImpact.High },
            { "brave",               StartupImpact.High },
            { "vivaldi",             StartupImpact.High },
            { "iexplore",            StartupImpact.Moderate },

            // ── Communication / collaboration (Electron) ──────────────────────
            // Electron shells are heavy: each loads a bundled Chromium+Node.js stack
            // and may poll notification servers continuously.
            { "teams",               StartupImpact.High },
            { "update",              StartupImpact.High }, // Teams updater stub
            { "discord",             StartupImpact.High },
            { "slack",               StartupImpact.High },
            { "zoom",                StartupImpact.High },
            { "skype",               StartupImpact.High },
            { "skypeapp",            StartupImpact.High },
            { "webex",               StartupImpact.High },
            { "msteams",             StartupImpact.High },
            { "telegram",            StartupImpact.Moderate },
            { "signal",              StartupImpact.Moderate },
            { "whatsapp",            StartupImpact.Moderate },

            // ── Gaming launchers ──────────────────────────────────────────────
            // Launchers stay resident to handle friend-list sync, download queues,
            // and update checks even when no game is running.
            { "steam",               StartupImpact.High },
            { "epicgameslauncher",   StartupImpact.High },
            { "galaxyclient",        StartupImpact.Moderate },  // GOG Galaxy
            { "battle.net",          StartupImpact.High },
            { "battlenet",           StartupImpact.High },
            { "origin",              StartupImpact.High },
            { "eadesktop",           StartupImpact.High },
            { "ubisoftconnect",      StartupImpact.Moderate },
            { "uplaywebcore",        StartupImpact.Moderate },

            // ── Media ─────────────────────────────────────────────────────────
            { "spotify",             StartupImpact.High },
            { "itunes",              StartupImpact.High },
            { "applemusic",          StartupImpact.Moderate },

            // ── Cloud storage / sync ──────────────────────────────────────────
            // Sync clients run continuously but are typically more lightweight
            // than Electron apps once the initial sync settles.
            { "onedrive",            StartupImpact.Moderate },
            { "dropbox",             StartupImpact.Moderate },
            { "googledrivesync",     StartupImpact.Moderate },
            { "googledrive",         StartupImpact.Moderate },
            { "box",                 StartupImpact.Moderate },
            { "boxsync",             StartupImpact.Moderate },
            { "icloudservices",      StartupImpact.Moderate },
            { "icloud",              StartupImpact.Moderate },
            { "megalauncher",        StartupImpact.Low },

            // ── Productivity suites ───────────────────────────────────────────
            { "outlook",             StartupImpact.High },
            { "thunderbird",         StartupImpact.Moderate },

            // ── Peripheral management ─────────────────────────────────────────
            // Tray utilities for gaming peripherals are often surprisingly heavy.
            { "razersurrogate",      StartupImpact.Moderate },
            { "razernativeplatform", StartupImpact.Moderate },
            { "razer",               StartupImpact.Moderate },
            { "logitechoptions",     StartupImpact.Moderate },
            { "logicooloptionsplus", StartupImpact.Moderate },
            { "lghub",               StartupImpact.Moderate },
            { "corsairhid",          StartupImpact.Moderate },
            { "icue",                StartupImpact.Moderate },
            { "steelseriesggtray",   StartupImpact.Low },

            // ── Updaters / background services ────────────────────────────────
            { "adobeupdater",        StartupImpact.Moderate },
            { "adobegcclient",       StartupImpact.Moderate },
            { "ccleaner",            StartupImpact.Moderate },
            { "malwarebytes",        StartupImpact.Moderate },
            { "mbam",                StartupImpact.Moderate },

            // ── Lightweight / native ──────────────────────────────────────────
            // These are either OS components or truly thin tray helpers.
            { "ctfmon",              StartupImpact.Low },
            { "sihost",              StartupImpact.Low },
            { "securityhealthsystray", StartupImpact.Low },
            { "realtek hd audio",    StartupImpact.Low },
            { "rkaudiservice64",     StartupImpact.Low },
            { "nvdisplay.container", StartupImpact.Low },
            { "amdrsserv",           StartupImpact.Low },
        };

    // ── Registry constants ────────────────────────────────────────────────────

    private const string RunKeyPath  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    // ── Sampling ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Enumerates all enabled startup entries visible to the current user.
    /// Returns an empty list on error rather than throwing.
    /// </summary>
    public IReadOnlyList<StartupEntry> Sample()
    {
        var results = new List<StartupEntry>(24);

        // HKCU – per-user entries (can be modified without elevation)
        ReadRegistryKey(Registry.CurrentUser, RunKeyPath,
                        StartupLocation.HkcuRun, results);

        // HKLM – machine-wide entries (modification typically requires elevation)
        ReadRegistryKey(Registry.LocalMachine, RunKeyPath,
                        StartupLocation.HklmRun, results);

        // Per-user Startup folder (.lnk shortcuts)
        ReadStartupFolder(results);

        return results;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void ReadRegistryKey(
        RegistryKey       hive,
        string            keyPath,
        StartupLocation   location,
        List<StartupEntry> results)
    {
        try
        {
            using var key = hive.OpenSubKey(keyPath, writable: false);
            if (key is null) return;

            foreach (var valueName in key.GetValueNames())
            {
                try
                {
                    var command = key.GetValue(valueName) as string;
                    if (string.IsNullOrWhiteSpace(command)) continue;

                    var exeName = ExtractExeName(command);
                    var impact  = ClassifyImpact(exeName);

                    results.Add(new StartupEntry(
                        Name:           valueName,
                        Command:        command,
                        ExecutableName: exeName,
                        Location:       location,
                        Impact:         impact));
                }
                catch
                {
                    // Skip individual values that fail to read.
                }
            }
        }
        catch
        {
            // Key inaccessible — skip silently.
        }
    }

    private static void ReadStartupFolder(List<StartupEntry> results)
    {
        try
        {
            var folderPath = Environment.GetFolderPath(
                Environment.SpecialFolder.Startup);

            if (!Directory.Exists(folderPath)) return;

            foreach (var file in Directory.EnumerateFiles(folderPath, "*.lnk"))
            {
                try
                {
                    var name    = Path.GetFileNameWithoutExtension(file);
                    var exeName = name.ToLowerInvariant();
                    var impact  = ClassifyImpact(exeName);

                    results.Add(new StartupEntry(
                        Name:           name,
                        Command:        file,
                        ExecutableName: exeName,
                        Location:       StartupLocation.StartupFolder,
                        Impact:         impact));
                }
                catch
                {
                    // Skip individual files that fail to read.
                }
            }
        }
        catch
        {
            // Folder inaccessible — skip silently.
        }
    }

    /// <summary>
    /// Extracts a lower-case exe filename (without extension) from a registry
    /// command string. Handles quoted paths and paths with arguments.
    ///
    /// Examples:
    ///   "C:\Program Files\Google\Chrome\Application\chrome.exe" --no-startup-window
    ///       → "chrome"
    ///   C:\Windows\System32\ctfmon.exe
    ///       → "ctfmon"
    /// </summary>
    private static string ExtractExeName(string command)
    {
        // Strip leading quote, then take everything up to the next quote or
        // the first space (whichever comes first) to isolate the exe path.
        var trimmed = command.TrimStart();
        string exePath;

        if (trimmed.StartsWith('"'))
        {
            int closing = trimmed.IndexOf('"', 1);
            exePath = closing > 0 ? trimmed[1..closing] : trimmed[1..];
        }
        else
        {
            int space = trimmed.IndexOf(' ');
            exePath = space > 0 ? trimmed[..space] : trimmed;
        }

        return Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
    }

    private static StartupImpact ClassifyImpact(string exeName) =>
        s_impactMap.TryGetValue(exeName, out var impact)
            ? impact
            : StartupImpact.Unknown;
}
