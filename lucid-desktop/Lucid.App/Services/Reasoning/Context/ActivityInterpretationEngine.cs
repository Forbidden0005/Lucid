namespace Lucid.Services.Reasoning.Context;

/// <summary>
/// Classifies the system's current primary activity from process and
/// telemetry signals using a ranked rule set.
///
/// The classifier uses process name matching and resource signatures
/// rather than opaque scoring to ensure all classifications are
/// explainable: "Gaming detected because [process] is running at high GPU."
///
/// Rules run in priority order; the first match wins.
/// Classification confidence reflects how well the signals match the rule.
///
/// This is a pure, stateless utility — all inputs are passed as parameters
/// so the output is fully deterministic and testable.
/// </summary>
public static class ActivityInterpretationEngine
{
    // ── Classification entry point ────────────────────────────────────────────

    /// <summary>
    /// Classifies the system's primary activity given telemetry readings and
    /// the set of currently running process names.
    /// </summary>
    /// <param name="cpuPercent">Current CPU utilization 0–100.</param>
    /// <param name="gpuPercent">Current GPU utilization 0–100.</param>
    /// <param name="diskThroughputMbps">Current disk throughput in MB/s.</param>
    /// <param name="ramPercent">Current RAM utilization 0–100.</param>
    /// <param name="uptimeSeconds">Seconds since last boot.</param>
    /// <param name="runningProcessNames">
    /// Lowercase process names currently running (no .exe extension needed).
    /// Never include full paths — process names only to avoid privacy leakage.
    /// </param>
    /// <param name="windowsUpdateActive">Whether Windows Update is actively running.</param>
    public static (ActivityClass Activity, double Confidence, string[] DriverProcesses)
        Classify(
            double         cpuPercent,
            double         gpuPercent,
            double         diskThroughputMbps,
            double         ramPercent,
            long           uptimeSeconds,
            IEnumerable<string> runningProcessNames,
            bool           windowsUpdateActive = false)
    {
        var processes = new HashSet<string>(
            runningProcessNames.Select(p => p.ToLowerInvariant().Replace(".exe", "")),
            StringComparer.OrdinalIgnoreCase);

        // ── Post-boot transient ───────────────────────────────────────────────
        if (uptimeSeconds < 180 && cpuPercent > 40)
            return (ActivityClass.Idle, 0.70, []);

        // ── Windows Update / system maintenance ───────────────────────────────
        if (windowsUpdateActive || HasAny(processes, "wuauclt", "windows update", "trustedinstaller", "msiexec"))
        {
            var conf = windowsUpdateActive ? 0.95 : 0.75;
            return (ActivityClass.SystemUpdate, conf, FilterPresent(processes, "trustedinstaller", "msiexec"));
        }

        // ── Backup ────────────────────────────────────────────────────────────
        if (diskThroughputMbps > 100 && HasAny(processes,
            "backupdaemon", "vssvc", "robocopy", "onedrive", "googledrivesync",
            "dropbox", "syncthing", "duplicati", "macrium", "backblaze", "carbonite"))
            return (ActivityClass.Backup, 0.80, FilterPresent(processes, "robocopy", "vssvc", "onedrive", "dropbox"));

        // ── Rendering / video encoding ────────────────────────────────────────
        if (cpuPercent > 80 && gpuPercent > 60 &&
            HasAny(processes, "blender", "premierepro", "afterfx", "vegas", "handbrake",
                              "ffmpeg", "resolve", "cinema4d", "maya", "nuke", "kdenlive"))
            return (ActivityClass.Rendering, 0.88, FilterPresent(processes,
                "blender", "premierepro", "afterfx", "handbrake", "ffmpeg"));

        // ── Gaming ────────────────────────────────────────────────────────────
        if (gpuPercent > 50 && HasAny(processes,
            "steam", "epicgameslauncher", "gog galaxy", "ea app", "battle.net",
            "riotclient", "roblox", "minecraft", "valorant", "csgo", "fortnite",
            "league of legends", "dota2", "overwatch", "destiny2", "apex"))
        {
            double conf = gpuPercent > 70 ? 0.92 : 0.75;
            return (ActivityClass.Gaming, conf, FilterPresent(processes, "steam", "epicgameslauncher"));
        }

        // ── Compilation ───────────────────────────────────────────────────────
        if (cpuPercent > 60 && HasAny(processes,
            "msbuild", "devenv", "vbcscompiler", "csc", "cl", "gcc", "clang",
            "rustc", "cargo", "go", "javac", "kotlinc", "gradle", "cmake",
            "node", "webpack", "vite", "tsc"))
        {
            double conf = cpuPercent > 80 ? 0.87 : 0.72;
            return (ActivityClass.Compiling, conf, FilterPresent(processes,
                "msbuild", "devenv", "vbcscompiler", "rustc", "cargo", "gcc"));
        }

        // ── Video streaming ───────────────────────────────────────────────────
        if (gpuPercent > 20 && HasAny(processes,
            "chrome", "firefox", "msedge", "opera", "brave", "vivaldi", "netflix", "vlc",
            "mpv", "mpc-hc", "potplayer", "plex", "emby", "jellyfin"))
        {
            if (cpuPercent < 50 && ramPercent < 70)
                return (ActivityClass.VideoStreaming, 0.65,
                    FilterPresent(processes, "chrome", "firefox", "msedge", "vlc", "mpv"));
        }

        // ── Browser-heavy ─────────────────────────────────────────────────────
        if (ramPercent > 65 && HasAny(processes, "chrome", "firefox", "msedge", "opera", "brave"))
        {
            int browserProcessCount = CountMatching(processes, "chrome", "firefox", "msedge");
            if (browserProcessCount >= 2)
                return (ActivityClass.BrowsingHeavy, 0.70, ["browser"]);
        }

        // ── Productivity ──────────────────────────────────────────────────────
        if (HasAny(processes, "winword", "excel", "powerpnt", "outlook", "onenote",
                              "code", "rider", "idea", "pycharm", "eclipse", "notepad++", "slack", "teams"))
            return (ActivityClass.Productivity, 0.62, FilterPresent(processes,
                "winword", "excel", "code", "rider", "slack", "teams"));

        // ── Idle ─────────────────────────────────────────────────────────────
        if (cpuPercent < 15 && gpuPercent < 10 && diskThroughputMbps < 10)
            return (ActivityClass.Idle, 0.80, []);

        // ── Mixed / unknown ───────────────────────────────────────────────────
        return cpuPercent > 30 || gpuPercent > 30
            ? (ActivityClass.Mixed, 0.40, [])
            : (ActivityClass.Unknown, 0.30, []);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool HasAny(HashSet<string> processes, params string[] names) =>
        names.Any(n => processes.Contains(n));

    private static string[] FilterPresent(HashSet<string> processes, params string[] names) =>
        names.Where(n => processes.Contains(n)).ToArray();

    private static int CountMatching(HashSet<string> processes, params string[] names) =>
        names.Count(n => processes.Contains(n));

    // ── Expected resource ranges per activity ─────────────────────────────────

    /// <summary>
    /// Returns the expected CPU range for the given activity class.
    /// Used by <see cref="OperationalContextSynthesizer"/> to build <see cref="ContextProfile"/>.
    /// </summary>
    public static (double Low, double High) ExpectedCpuRange(ActivityClass activity) =>
        activity switch
        {
            ActivityClass.Gaming         => (30, 90),
            ActivityClass.Compiling      => (60, 100),
            ActivityClass.Rendering      => (70, 100),
            ActivityClass.SystemUpdate   => (20, 80),
            ActivityClass.Backup         => (10, 40),
            ActivityClass.VideoStreaming => (10, 40),
            ActivityClass.BrowsingHeavy  => (10, 50),
            ActivityClass.Productivity   => (5, 30),
            ActivityClass.Idle           => (0, 15),
            _                            => (0, 100),
        };

    /// <summary>Returns the expected thermal range (°C) for the given activity class.</summary>
    public static (double Low, double High) ExpectedThermalRange(ActivityClass activity) =>
        activity switch
        {
            ActivityClass.Gaming         => (60, 90),
            ActivityClass.Compiling      => (65, 95),
            ActivityClass.Rendering      => (65, 95),
            ActivityClass.SystemUpdate   => (50, 80),
            ActivityClass.Backup         => (40, 70),
            ActivityClass.VideoStreaming => (45, 70),
            ActivityClass.Idle           => (30, 55),
            _                            => (30, 100),
        };
}
