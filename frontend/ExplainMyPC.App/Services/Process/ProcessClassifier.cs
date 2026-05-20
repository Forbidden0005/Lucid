namespace ExplainMyPC.Services.ProcessIntel;

/// <summary>
/// Classifies a process into a <see cref="ProcessCategory"/> and determines
/// whether it is system-critical (must never be terminated by the user).
/// </summary>
internal static class ProcessClassifier
{
    // ── Category map — keyed by process name (no extension, lowercase) ────────

    private static readonly Dictionary<string, ProcessCategory> s_categories =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // Browsers
        ["chrome"] = ProcessCategory.Browser, ["msedge"] = ProcessCategory.Browser,
        ["firefox"] = ProcessCategory.Browser, ["opera"] = ProcessCategory.Browser,
        ["brave"] = ProcessCategory.Browser, ["iexplore"] = ProcessCategory.Browser,
        ["vivaldi"] = ProcessCategory.Browser, ["arc"] = ProcessCategory.Browser,

        // Electron apps (Chromium-based desktop apps)
        ["discord"] = ProcessCategory.ElectronApp,
        ["slack"] = ProcessCategory.ElectronApp,
        ["teams"] = ProcessCategory.ElectronApp,
        ["msteams"] = ProcessCategory.ElectronApp,
        ["zoom"] = ProcessCategory.Communication,
        ["skype"] = ProcessCategory.Communication,

        // Developer tools
        ["code"] = ProcessCategory.DeveloperTool,
        ["devenv"] = ProcessCategory.DeveloperTool,
        ["rider64"] = ProcessCategory.DeveloperTool,
        ["idea64"] = ProcessCategory.DeveloperTool,
        ["pycharm64"] = ProcessCategory.DeveloperTool,
        ["webstorm64"] = ProcessCategory.DeveloperTool,
        ["clion64"] = ProcessCategory.DeveloperTool,
        ["git"] = ProcessCategory.DeveloperTool,
        ["node"] = ProcessCategory.Runtime,
        ["python"] = ProcessCategory.Runtime, ["python3"] = ProcessCategory.Runtime,
        ["java"] = ProcessCategory.Runtime, ["javaw"] = ProcessCategory.Runtime,
        ["dotnet"] = ProcessCategory.Runtime,
        ["powershell"] = ProcessCategory.DeveloperTool,
        ["cmd"] = ProcessCategory.DeveloperTool,
        ["wt"] = ProcessCategory.DeveloperTool,

        // Games / launchers
        ["steam"] = ProcessCategory.Game,
        ["steamwebhelper"] = ProcessCategory.Game,
        ["epicgameslauncher"] = ProcessCategory.Game,
        ["gog galaxy"] = ProcessCategory.Game,

        // Media
        ["obs64"] = ProcessCategory.MediaPlayer, ["obs32"] = ProcessCategory.MediaPlayer,
        ["vlc"] = ProcessCategory.MediaPlayer,
        ["wmplayer"] = ProcessCategory.MediaPlayer,
        ["spotify"] = ProcessCategory.MediaPlayer,
        ["itunes"] = ProcessCategory.MediaPlayer,

        // Cloud sync / updaters
        ["onedrive"] = ProcessCategory.CloudSync,
        ["dropbox"] = ProcessCategory.CloudSync,
        ["googledrivefs"] = ProcessCategory.CloudSync,
        ["boxsync"] = ProcessCategory.CloudSync,
        ["wuauclt"] = ProcessCategory.Updater,
        ["musnotification"] = ProcessCategory.Updater,
        ["updatenotificationmgr"] = ProcessCategory.Updater,
        ["setuphost"] = ProcessCategory.Updater,

        // Office
        ["winword"] = ProcessCategory.Office, ["excel"] = ProcessCategory.Office,
        ["powerpnt"] = ProcessCategory.Office, ["outlook"] = ProcessCategory.Office,
        ["onenote"] = ProcessCategory.Office, ["teams"] = ProcessCategory.Communication,

        // Security
        ["msmpeng"] = ProcessCategory.Security,
        ["msseces"] = ProcessCategory.Security,
        ["mbam"] = ProcessCategory.Security,
        ["avgui"] = ProcessCategory.Security,

        // Services
        ["svchost"] = ProcessCategory.Service,
        ["services"] = ProcessCategory.Service,
        ["taskhost"] = ProcessCategory.Service,
        ["taskhostw"] = ProcessCategory.Service,
        ["runtimebroker"] = ProcessCategory.Service,
        ["dllhost"] = ProcessCategory.Service,
        ["conhost"] = ProcessCategory.Service,
    };

    // ── Critical process names — NEVER allow user termination ─────────────────

    private static readonly HashSet<string> s_critical =
        new(StringComparer.OrdinalIgnoreCase)
    {
        "csrss", "wininit", "winlogon", "smss", "lsass", "lsm",
        "services", "svchost", "system", "registry", "dwm",
        "fontdrvhost", "msmpeng", "securityhealthservice",
        "spoolsv", "audiodg",
    };

    internal static ProcessCategory Classify(string processName)
    {
        if (s_categories.TryGetValue(processName, out var cat)) return cat;

        // Heuristic suffix rules
        var lc = processName.ToLowerInvariant();
        if (lc.Contains("update") || lc.Contains("updater")) return ProcessCategory.Updater;
        if (lc.Contains("service") || lc.EndsWith("svc"))   return ProcessCategory.Service;
        if (lc.Contains("helper") || lc.Contains("agent"))  return ProcessCategory.Service;

        return ProcessCategory.Unknown;
    }

    internal static bool IsCritical(string processName) =>
        s_critical.Contains(processName);
}
