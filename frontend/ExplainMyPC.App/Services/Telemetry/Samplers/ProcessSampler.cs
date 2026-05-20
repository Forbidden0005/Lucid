using System.Diagnostics;
using ExplainMyPC.Helpers;

namespace ExplainMyPC.Services.Telemetry.Samplers;

/// <summary>
/// Samples CPU and RAM usage for all running processes.
///
/// CPU calculation:
///   Uses the two-point delta method — CPU% = (cpuTimeDelta) /
///   (wallClockDelta × processorCount) × 100 — so the first call always
///   returns 0 % for all processes (no previous data point).
///   Subsequent calls produce accurate cross-core-normalised values in [0, 100].
///
/// Result:
///   Returns the union of the top-N processes by CPU and top-N by RAM,
///   deduplicated by PID. This ensures both high-CPU and high-RAM outliers
///   appear even when they don't overlap.
///
/// Thread safety:
///   <see cref="Sample"/> is called exclusively from the WindowsTelemetryService
///   background thread. No synchronisation is needed.
///
/// Lifetime:
///   Owned by <see cref="WindowsTelemetryService"/>; disposed when the service stops.
///   No unmanaged resources are held — Dispose is a no-op.
/// </summary>
internal sealed class ProcessSampler : IDisposable
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>Top-N by CPU and top-N by RAM are unioned before deduplication.</summary>
    private const int TopN = 10;

    // ── Known display names ───────────────────────────────────────────────────
    //    Keyed by Process.ProcessName (case-insensitive, no extension).
    //    Unknown processes fall back to Title-Cased ProcessName.

    private static readonly Dictionary<string, string> s_displayNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // ── Browsers ──────────────────────────────────────────────────
            ["chrome"]             = "Google Chrome",
            ["msedge"]             = "Microsoft Edge",
            ["firefox"]            = "Firefox",
            ["opera"]              = "Opera",
            ["iexplore"]           = "Internet Explorer",
            ["brave"]              = "Brave",
            ["vivaldi"]            = "Vivaldi",
            ["arc"]                = "Arc Browser",

            // ── Communication ────────────────────────────────────────────
            ["discord"]            = "Discord",
            ["slack"]              = "Slack",
            ["teams"]              = "Microsoft Teams",
            ["zoom"]               = "Zoom",
            ["skype"]              = "Skype",
            ["msteams"]            = "Microsoft Teams",

            // ── Media / Streaming ────────────────────────────────────────
            ["obs64"]              = "OBS Studio",
            ["obs32"]              = "OBS Studio",
            ["vlc"]                = "VLC Media Player",
            ["wmplayer"]           = "Windows Media Player",
            ["spotify"]            = "Spotify",
            ["itunes"]             = "iTunes",

            // ── Developer Tools ──────────────────────────────────────────
            ["code"]               = "Visual Studio Code",
            ["devenv"]             = "Visual Studio",
            ["rider64"]            = "JetBrains Rider",
            ["idea64"]             = "IntelliJ IDEA",
            ["pycharm64"]          = "PyCharm",
            ["webstorm64"]         = "WebStorm",
            ["clion64"]            = "CLion",
            ["goland64"]           = "GoLand",

            // ── Gaming / Distribution ────────────────────────────────────
            ["steam"]              = "Steam",
            ["steamwebhelper"]     = "Steam Web Helper",
            ["epicgameslauncher"]  = "Epic Games Launcher",
            ["gog galaxy"]         = "GOG Galaxy",

            // ── Windows System ───────────────────────────────────────────
            ["explorer"]           = "Windows Explorer",
            ["dwm"]                = "Desktop Window Manager",
            ["searchhost"]         = "Windows Search",
            ["searchindexer"]      = "Windows Search Indexer",
            ["msmpeng"]            = "Windows Defender",
            ["svchost"]            = "Windows Service Host",
            ["taskmgr"]            = "Task Manager",
            ["RuntimeBroker"]      = "Runtime Broker",
            ["wuauclt"]            = "Windows Update",
            ["wuaserv"]            = "Windows Update Service",
            ["audiodg"]            = "Windows Audio",
            ["csrss"]              = "Client Server Runtime",
            ["lsass"]              = "Local Security Authority",

            // ── Cloud Storage ────────────────────────────────────────────
            ["onedrive"]           = "OneDrive",
            ["dropbox"]            = "Dropbox",
            ["googledrivefs"]      = "Google Drive",
            ["boxsync"]            = "Box Sync",

            // ── Microsoft Office ─────────────────────────────────────────
            ["winword"]            = "Microsoft Word",
            ["excel"]              = "Microsoft Excel",
            ["powerpnt"]           = "Microsoft PowerPoint",
            ["outlook"]            = "Microsoft Outlook",
            ["onenote"]            = "Microsoft OneNote",
            ["mspub"]              = "Microsoft Publisher",
            ["msaccess"]           = "Microsoft Access",

            // ── Runtimes ─────────────────────────────────────────────────
            ["python"]             = "Python",
            ["python3"]            = "Python",
            ["node"]               = "Node.js",
            ["java"]               = "Java",
            ["javaw"]              = "Java",
            ["dotnet"]             = ".NET Runtime",
            ["powershell"]         = "PowerShell",
            ["cmd"]                = "Command Prompt",
        };

    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Previous poll data — keyed by PID, value is (total CPU time, wall clock).
    /// Null on the very first call so the result is an empty list of CPU deltas.
    /// </summary>
    private Dictionary<int, (TimeSpan CpuTime, DateTime WallClock)>? _previous;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Enumerates all running processes, calculates CPU delta since the last call,
    /// and returns the union of the top-N by CPU usage and top-N by RAM usage.
    ///
    /// Always call from the telemetry background thread; never from the UI thread.
    /// Returns an empty list on the first call (no previous data point for CPU delta).
    /// </summary>
    public IReadOnlyList<ProcessSample> Sample()
    {
        var    now      = DateTime.UtcNow;
        int    cpuCount = Environment.ProcessorCount;

        Process[] all;
        try { all = Process.GetProcesses(); }
        catch { return []; }

        var current = new Dictionary<int, (TimeSpan, DateTime)>(all.Length);
        var samples  = new List<(int Id, string Name, string Display, float CpuPct, long Ram)>(all.Length);

        foreach (var proc in all)
        {
            try
            {
                int      pid  = proc.Id;
                string   name = proc.ProcessName;
                long     ram  = proc.WorkingSet64;
                TimeSpan cpu  = proc.TotalProcessorTime;

                current[pid] = (cpu, now);

                float cpuPct = 0f;
                if (_previous is not null && _previous.TryGetValue(pid, out var prev))
                {
                    double elapsed = (now - prev.WallClock).TotalSeconds;
                    if (elapsed > 0)
                    {
                        double delta = (cpu - prev.CpuTime).TotalSeconds;
                        cpuPct = (float)Math.Clamp(delta / (elapsed * cpuCount) * 100.0, 0, 100);
                    }
                }

                samples.Add((pid, name, MapDisplayName(name), cpuPct, ram));
            }
            catch
            {
                // Process exited between GetProcesses() and the property reads. Skip.
            }
            finally
            {
                proc.Dispose();
            }
        }

        _previous = current;

        if (samples.Count == 0)
            return [];

        // Union of top-N by CPU and top-N by RAM, deduplicated by PID.
        // This ensures high-RAM apps (browsers) appear even at low CPU.
        var byCpu = samples.OrderByDescending(s => s.CpuPct).Take(TopN);
        var byRam = samples.OrderByDescending(s => s.Ram).Take(TopN);

        return byCpu.UnionBy(byRam, s => s.Id)
                    .Select(s => new ProcessSample(s.Id, s.Name, s.Display, s.CpuPct, s.Ram))
                    .ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string MapDisplayName(string processName) =>
        s_displayNames.TryGetValue(processName, out var display)
            ? display
            : System.Globalization.CultureInfo.CurrentCulture.TextInfo
                    .ToTitleCase(processName.Replace('-', ' ').Replace('_', ' '));

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <summary>No unmanaged resources; dispose is a no-op.</summary>
    public void Dispose() { }
}
