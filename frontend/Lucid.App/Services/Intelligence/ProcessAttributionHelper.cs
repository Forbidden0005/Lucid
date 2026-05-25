using Lucid.Helpers;

namespace Lucid.Services.Intelligence;

/// <summary>
/// Produces <see cref="ProcessAttribution"/> lists from a process snapshot.
///
/// Called by insight rules immediately before constructing a <see cref="SystemInsight"/>
/// so findings can include "which process caused this" attribution.
///
/// CPU and RAM attributions use direct measurement (confidence 90 %).
/// GPU attributions are heuristic — inferred from known GPU-intensive app names
/// when per-process GPU counters are unavailable (confidence 55 %).
///
/// Rules run synchronously on the UI thread; these helpers are pure LINQ
/// projections over an already-collected list — no I/O or blocking work.
/// </summary>
public static class ProcessAttributionHelper
{
    // ── Thresholds ─────────────────────────────────────────────────────────────

    /// <summary>Minimum CPU% for a process to appear in a CPU attribution list.</summary>
    private const float MinCpuPercent = 5f;

    /// <summary>Minimum Working Set for a process to appear in a RAM attribution list (100 MB).</summary>
    private const long MinRamBytes = 100L * 1024 * 1024;

    /// <summary>Maximum attributions returned per insight to keep the UI compact.</summary>
    private const int MaxAttributions = 3;

    // ── Known GPU-intensive process names ──────────────────────────────────────
    //    Used for heuristic GPU attribution. Keep lowercase to match the case-
    //    insensitive lookup. DWM always uses GPU for compositing.

    private static readonly HashSet<string> s_gpuIntensive =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "chrome",          // Google Chrome
            "msedge",          // Microsoft Edge
            "firefox",         // Firefox
            "opera",           // Opera
            "brave",           // Brave Browser

            "discord",         // Discord (uses Chromium renderer with GPU accel)
            "slack",           // Slack
            "teams",           // Microsoft Teams
            "zoom",            // Zoom

            "obs64",           // OBS Studio (64-bit)
            "obs32",           // OBS Studio (32-bit)
            "vlc",             // VLC Media Player
            "wmplayer",        // Windows Media Player

            "steamwebhelper",  // Steam browser helper (Chromium)
            "steam",           // Steam client

            "dwm",             // Desktop Window Manager (always GPU)
        };

    // ── Public factory methods ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the top CPU-consuming processes contributing to an elevated
    /// CPU insight. Only processes at or above <c>5 %</c> CPU are included.
    /// Attribution confidence is 90 % (directly measured).
    /// </summary>
    public static IReadOnlyList<ProcessAttribution> ForCpu(
        IReadOnlyList<ProcessSample>? processes)
    {
        if (processes is null or { Count: 0 })
            return [];

        return processes
            .Where(p => p.CpuPercent >= MinCpuPercent)
            .OrderByDescending(p => p.CpuPercent)
            .Take(MaxAttributions)
            .Select(p => new ProcessAttribution(
                DisplayName:       p.DisplayName,
                Kind:              AttributionKind.Cpu,
                UsageValue:        p.CpuPercent,
                UsageUnit:         "%",
                ConfidencePercent: 90))
            .ToList();
    }

    /// <summary>
    /// Returns the top RAM-consuming processes contributing to a high-memory
    /// pressure insight. Only processes using at least 100 MB are included.
    /// Usage is expressed in GB. Attribution confidence is 90 %.
    /// </summary>
    public static IReadOnlyList<ProcessAttribution> ForRam(
        IReadOnlyList<ProcessSample>? processes,
        double ramTotalGb)
    {
        if (processes is null or { Count: 0 } || ramTotalGb <= 0)
            return [];

        return processes
            .Where(p => p.RamBytes >= MinRamBytes)
            .OrderByDescending(p => p.RamBytes)
            .Take(MaxAttributions)
            .Select(p =>
            {
                double gb = Math.Round(p.RamBytes / (1024.0 * 1024.0 * 1024.0), 1);
                return new ProcessAttribution(
                    DisplayName:       p.DisplayName,
                    Kind:              AttributionKind.Ram,
                    UsageValue:        (float)gb,
                    UsageUnit:         "GB",
                    ConfidencePercent: 90);
            })
            .ToList();
    }

    /// <summary>
    /// Returns processes heuristically identified as GPU consumers by checking
    /// whether their name appears in the known GPU-intensive app list.
    ///
    /// Per-process GPU counter data is not available from standard .NET perf
    /// counters, so this is always an inference — confidence is 55 %.
    /// UsageValue is 0 for all GPU attributions; the UI renders "Detected".
    /// </summary>
    public static IReadOnlyList<ProcessAttribution> ForGpu(
        IReadOnlyList<ProcessSample>? processes)
    {
        if (processes is null or { Count: 0 })
            return [];

        return processes
            .Where(p => s_gpuIntensive.Contains(p.ProcessName))
            .Take(MaxAttributions)
            .Select(p => new ProcessAttribution(
                DisplayName:       p.DisplayName,
                Kind:              AttributionKind.Gpu,
                UsageValue:        0,          // unknown actual GPU % — heuristic only
                UsageUnit:         "%",
                ConfidencePercent: 55))
            .ToList();
    }
}
