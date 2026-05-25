using Lucid.Helpers;
using Lucid.Services.Session;

namespace Lucid.Services.Behavior;

/// <summary>
/// Deterministic, heuristic-based workload classifier.
///
/// Analyzes the current telemetry snapshot and process composition to classify
/// the active workload type. All logic is local, explainable, and confidence-aware.
/// No ML, no cloud, no probabilistic models.
///
/// Classification priority (highest wins first):
///   Idle → StartupHeavy → Gaming → Rendering → MediaEditing → Development →
///   Streaming → BrowserResearch → Productivity → ThermalIntensive →
///   OvernightProcessing → BackgroundProcessing → Unknown
///
/// Confidence levels:
///   High   — multiple strong signals corroborate the classification
///   Medium — one primary signal or partial match
///   Low    — weak or conflicting evidence
/// </summary>
public static class SessionClassifier
{
    // ── Process name sets (no extension, lowercase) ───────────────────────────

    private static readonly HashSet<string> s_games = new(StringComparer.OrdinalIgnoreCase)
    {
        // Launchers
        "steam", "steamwebhelper", "epicgameslauncher", "galaxyclient",
        "battlenet", "riotclientservices", "riotclientux",
        // Known titles
        "leagueclient", "leagueclientux", "valorant-win64-shipping", "csgo", "cs2",
        "gta5", "gtavlauncher", "fortnite", "fortniteclient-win64-shipping",
        "minecraft", "javaw",           // Minecraft uses javaw
        "dota2", "r5apex",              // Dota 2, Apex Legends
        "destiny2", "stars_d3d",        // Destiny 2
        "overwatch", "overwatch2",
        "hearthstone", "battlelog",
        // GPU overlay — strong gaming signal
        "nvcontainer", "nvdisplay.container", "gameoverlayui",
        "nvcplui", "RTSSHooksLoader64",
    };

    private static readonly HashSet<string> s_rendering = new(StringComparer.OrdinalIgnoreCase)
    {
        "blender", "blender-launcher",
        "handbrake", "handbrakecli",
        "ffmpeg", "ffprobe",
        "resolve",                      // DaVinci Resolve
        "revit", "revitlmv",
        "3dsmax", "3dsmaxio",
        "maya", "maya2",
        "cinema4d", "c4d",
        "houdini", "houdinifx",
        "nuke", "nukestudio",
        "encoder-amd-pro",
        "nvencodecomapi",
    };

    private static readonly HashSet<string> s_mediaEditing = new(StringComparer.OrdinalIgnoreCase)
    {
        "premiere", "adobepremiere",
        "aftereffects",
        "audition",
        "photoshop",
        "lightroom", "lightroomclassic",
        "illustrator",
        "indesign",
        "gimp",
        "krita",
        "inkscape",
        "vegas", "vegaspro",
        "kdenlive",
        "obs64", "obs32",               // OBS — capture/streaming
        "obs", "streamlabs",
        "audacity",
        "davinci",
    };

    private static readonly HashSet<string> s_devTools = new(StringComparer.OrdinalIgnoreCase)
    {
        // IDEs
        "devenv",                       // Visual Studio
        "code",                         // VS Code
        "rider64", "rider",
        "idea64", "idea",               // IntelliJ IDEA
        "pycharm64", "pycharm",
        "webstorm64", "webstorm",
        "clion64", "clion",
        "goland64", "goland",
        "eclipse",
        "netbeans64",
        // Terminals / shells
        "wt",                           // Windows Terminal
        "powershell", "pwsh",
        "cmd",
        "bash", "zsh", "fish",
        "mintty",                       // Git Bash
        // Compilers / build tools
        "msbuild", "xbuild",
        "cmake", "ninja", "make",
        "gradle", "gradlew",
        "dotnet",
        "node", "nodejs",
        "python", "python3", "python39", "python310", "python311", "python312",
        "java", "javac",
        "rustup", "cargo",
        "tsc",                          // TypeScript compiler
        "npm", "yarn", "pnpm",
        "git", "git-lfs",
        // Containers / VMs
        "docker", "dockerd", "docker desktop", "dockerdesktop",
        "wsl", "wsl2",
        "virtualbox", "vmware-vmx",
        "vboxmanage",
        // Other
        "vim", "nvim", "gvim",
        "emacs",
        "deno",
    };

    private static readonly HashSet<string> s_browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "chromium",
        "msedge",
        "firefox",
        "opera", "opera_gx",
        "brave",
        "vivaldi",
        "arc",
        "iexplore",
        "waterfox",
    };

    private static readonly HashSet<string> s_mediaPlayers = new(StringComparer.OrdinalIgnoreCase)
    {
        "vlc",
        "wmplayer",
        "mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64",
        "groove",
        "zune",
        "spotify",
        "itunes",
        "foobar2000",
        "aimp",
        "potplayer",
        "potplayermini", "potplayermini64",
        "bsplayer",
        "mpv",
        "winamp",
    };

    private static readonly HashSet<string> s_office = new(StringComparer.OrdinalIgnoreCase)
    {
        "winword",
        "excel",
        "powerpnt",
        "outlook",
        "onenote",
        "access",
        "lync",
        "teams", "msteams",
        "zoom",
        "skype",
        "slack",
        "discord",
        "notion",
        "obsidian",
        "evernote",
    };

    // ── Thresholds ─────────────────────────────────────────────────────────────

    private const double IdleCpuThreshold   = 10.0;   // below → Idle
    private const double IdleRamThreshold   = 40.0;

    private const double CpuHigh            = 70.0;
    private const double CpuMed             = 30.0;

    private const double RamHigh            = 70.0;
    private const double RamMed             = 50.0;

    private const double GpuHigh            = 60.0;
    private const double GpuMed             = 30.0;

    private const double ThermalHigh        = 80.0;   // °C

    // ── Public classification API ──────────────────────────────────────────────

    /// <summary>
    /// Classifies the current workload from telemetry and session context.
    /// All returned fields are deterministic — the rationale explains exactly
    /// which signals drove the classification.
    /// </summary>
    public static WorkloadClassification Classify(
        TelemetrySnapshot snap,
        SessionContext?   session)
    {
        var signals = new List<string>(8);
        var procs   = snap.TopProcesses;
        var now     = DateTimeOffset.Now;
        int hour    = now.LocalDateTime.Hour;

        // Count processes by behavioral category
        int games   = 0, renders = 0, editors = 0;
        int devs    = 0, browsers = 0, media  = 0, office = 0;

        foreach (var p in procs)
        {
            var n = p.ProcessName;
            if (s_games.Contains(n))        games++;
            if (s_rendering.Contains(n))    renders++;
            if (s_mediaEditing.Contains(n)) editors++;
            if (s_devTools.Contains(n))     devs++;
            if (s_browsers.Contains(n))     browsers++;
            if (s_mediaPlayers.Contains(n)) media++;
            if (s_office.Contains(n))       office++;
        }

        // ── 1. Idle ────────────────────────────────────────────────────────────
        if (snap.CpuPercent < IdleCpuThreshold &&
            snap.RamPercent < IdleRamThreshold  &&
            (session is null || (!session.IsPostBoot && !session.IsStartupSettling)))
        {
            signals.Add($"CPU at {snap.CpuPercent:F0}% — below idle threshold");
            signals.Add($"RAM at {snap.RamPercent:F0}%");
            return Make(WorkloadType.Idle, WorkloadType.Unknown, BehaviorConfidence.High,
                "System is quiet — no active user workload detected.", signals, now);
        }

        // ── 2. Startup-heavy ──────────────────────────────────────────────────
        if (session?.IsPostBoot == true || session?.IsStartupSettling == true)
        {
            signals.Add(session.IsPostBoot
                ? $"System uptime only {session.SystemUptime.TotalMinutes:F0} minutes"
                : "App launched recently — startup apps are loading");
            signals.Add($"CPU at {snap.CpuPercent:F0}%");
            return Make(WorkloadType.StartupHeavy, WorkloadType.Unknown, BehaviorConfidence.High,
                "Startup load — OS services and applications are still initializing.", signals, now);
        }

        // ── 3. Gaming ─────────────────────────────────────────────────────────
        if (games >= 1)
        {
            bool gpuHeavy    = snap.GpuAvailable && snap.GpuPercent > GpuMed;
            bool highLoad    = snap.CpuPercent > CpuMed;
            bool highTemp    = snap.CpuTemperatureAvailable && snap.CpuTemperatureCelsius > ThermalHigh;

            var conf = (gpuHeavy && highLoad) ? BehaviorConfidence.High
                     : gpuHeavy              ? BehaviorConfidence.Medium
                     : BehaviorConfidence.Medium;

            signals.Add($"{games} game-related process(es) detected");
            if (gpuHeavy) signals.Add($"GPU at {snap.GpuPercent:F0}%");
            if (highTemp) signals.Add($"CPU temperature {snap.CpuTemperatureCelsius:F0}°C");

            var secondary = editors >= 1 ? WorkloadType.Streaming
                          : devs >= 1    ? WorkloadType.Development
                          : WorkloadType.Unknown;

            return Make(WorkloadType.Gaming, secondary, conf,
                $"Gaming session detected — {games} game process(es) active" +
                (gpuHeavy ? $", GPU at {snap.GpuPercent:F0}%." : "."),
                signals, now);
        }

        // ── 4. Rendering (CPU-saturating encoder) ─────────────────────────────
        if (renders >= 1 || (editors >= 1 && snap.CpuPercent > CpuHigh))
        {
            signals.Add(renders >= 1
                ? $"{renders} render/encode process(es)"
                : "Media editor running at high CPU");
            signals.Add($"CPU at {snap.CpuPercent:F0}%");

            return Make(WorkloadType.Rendering, WorkloadType.MediaEditing, BehaviorConfidence.High,
                $"Rendering or encoding workload — sustained CPU at {snap.CpuPercent:F0}%.",
                signals, now);
        }

        // ── 5. Media editing ──────────────────────────────────────────────────
        if (editors >= 1)
        {
            var conf = snap.RamPercent > RamMed ? BehaviorConfidence.High : BehaviorConfidence.Medium;
            signals.Add($"{editors} media editing tool(s) active");
            signals.Add($"RAM at {snap.RamPercent:F0}%");

            return Make(WorkloadType.MediaEditing, WorkloadType.Unknown, conf,
                $"Media editing session — {editors} creative tool(s) open.", signals, now);
        }

        // ── 6. Development ────────────────────────────────────────────────────
        if (devs >= 2)
        {
            signals.Add($"{devs} developer tools running");
            signals.Add($"RAM at {snap.RamPercent:F0}%");

            var secondary = browsers >= 1 ? WorkloadType.BrowserResearch : WorkloadType.Unknown;
            return Make(WorkloadType.Development, secondary, BehaviorConfidence.High,
                $"Active development session — {devs} developer tools detected.", signals, now);
        }

        if (devs == 1)
        {
            signals.Add("1 developer tool process");
            var secondary = browsers >= 2 ? WorkloadType.BrowserResearch : WorkloadType.Unknown;
            return Make(WorkloadType.Development, secondary, BehaviorConfidence.Medium,
                "Likely development — one developer tool is open.", signals, now);
        }

        // ── 7. Streaming / media consumption ─────────────────────────────────
        if (media >= 1 && snap.GpuAvailable && snap.GpuPercent > GpuMed)
        {
            signals.Add($"{media} media player(s)");
            signals.Add($"GPU at {snap.GpuPercent:F0}%");

            return Make(WorkloadType.Streaming, WorkloadType.Unknown, BehaviorConfidence.High,
                "Media consumption — video or audio playback with GPU activity.", signals, now);
        }

        // ── 8. Browser-heavy research ─────────────────────────────────────────
        if (browsers >= 2 && snap.RamPercent > RamMed)
        {
            var conf = (browsers >= 3 && snap.RamPercent > RamHigh)
                       ? BehaviorConfidence.High : BehaviorConfidence.Medium;
            signals.Add($"{browsers} browser processes");
            signals.Add($"RAM at {snap.RamPercent:F0}%");

            var secondary = office >= 1 ? WorkloadType.Productivity : WorkloadType.Unknown;
            return Make(WorkloadType.BrowserResearch, secondary, conf,
                $"Browser-heavy session — {browsers} browser processes, RAM at {snap.RamPercent:F0}%.",
                signals, now);
        }

        // ── 9. Productivity ───────────────────────────────────────────────────
        if (office >= 1)
        {
            signals.Add($"{office} productivity/communication app(s)");
            signals.Add($"CPU at {snap.CpuPercent:F0}%");

            return Make(WorkloadType.Productivity, WorkloadType.Unknown, BehaviorConfidence.Medium,
                $"Productivity session — {office} office or communication app(s) in use.", signals, now);
        }

        // ── 10. Thermal-intensive (hot but workload unrecognized) ─────────────
        if (snap.CpuTemperatureAvailable     &&
            snap.CpuTemperatureCelsius > ThermalHigh &&
            snap.CpuPercent > CpuMed)
        {
            signals.Add($"Temperature at {snap.CpuTemperatureCelsius:F0}°C");
            signals.Add($"CPU at {snap.CpuPercent:F0}%");

            return Make(WorkloadType.ThermalIntensive, WorkloadType.Unknown, BehaviorConfidence.Medium,
                $"Thermal-intensive workload — temperature elevated at {snap.CpuTemperatureCelsius:F0}°C.",
                signals, now);
        }

        // ── 11. Overnight processing ──────────────────────────────────────────
        if ((hour >= 22 || hour <= 5) && snap.CpuPercent > CpuMed)
        {
            signals.Add($"System active at {now.LocalDateTime:h:mm tt}");
            signals.Add($"CPU at {snap.CpuPercent:F0}%");

            return Make(WorkloadType.OvernightProcessing, WorkloadType.Unknown, BehaviorConfidence.Medium,
                "Overnight session — system active during late hours.", signals, now);
        }

        // ── 12. Background processing (active CPU, no recognized signature) ───
        if (snap.CpuPercent > CpuMed)
        {
            signals.Add($"CPU at {snap.CpuPercent:F0}% — no foreground workload signature");

            return Make(WorkloadType.BackgroundProcessing, WorkloadType.Unknown, BehaviorConfidence.Low,
                $"Background activity — CPU at {snap.CpuPercent:F0}% with no recognized workload pattern.",
                signals, now);
        }

        // ── Fallback ──────────────────────────────────────────────────────────
        signals.Add($"CPU {snap.CpuPercent:F0}%, RAM {snap.RamPercent:F0}%");
        return Make(WorkloadType.Unknown, WorkloadType.Unknown, BehaviorConfidence.Low,
            "Workload pattern not recognized from current signals.", signals, now);
    }

    // ── Fingerprint capture ────────────────────────────────────────────────────

    /// <summary>
    /// Captures a lightweight, point-in-time usage fingerprint from the
    /// current telemetry snapshot.
    /// </summary>
    public static UsageFingerprint CaptureFingerprint(TelemetrySnapshot snap)
    {
        var procs    = snap.TopProcesses;
        int browsers = 0, devs = 0, games = 0, media = 0;

        foreach (var p in procs)
        {
            var n = p.ProcessName;
            if (s_browsers.Contains(n))     browsers++;
            if (s_devTools.Contains(n))     devs++;
            if (s_games.Contains(n))        games++;
            if (s_mediaPlayers.Contains(n)) media++;
        }

        return new UsageFingerprint(
            AvgCpuPct:           snap.CpuPercent,
            AvgRamPct:           snap.RamPercent,
            AvgThermalC:         snap.CpuTemperatureAvailable ? snap.CpuTemperatureCelsius : 0,
            ActiveProcessCount:  procs.Count,
            BrowserProcessCount: browsers,
            DevToolProcessCount: devs,
            GameProcessCount:    games,
            MediaProcessCount:   media,
            GpuPercent:          snap.GpuAvailable ? snap.GpuPercent : 0,
            CapturedAt:          DateTimeOffset.Now);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static WorkloadClassification Make(
        WorkloadType       primary,
        WorkloadType       secondary,
        BehaviorConfidence confidence,
        string             rationale,
        List<string>       signals,
        DateTimeOffset     at) =>
        new(primary, secondary, confidence, rationale, at, signals);
}
