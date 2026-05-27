using Lucid.Helpers;
using Lucid.Services.Startup;
using Lucid.Services.Telemetry;

namespace Lucid.Services.Intelligence.Rules;

/// <summary>
/// Fires when high-impact startup applications are present AND the system is
/// currently experiencing elevated resource pressure on CPU or disk I/O.
///
/// This rule correlates the startup configuration with live telemetry to
/// produce a more actionable finding than either piece of information alone.
/// The pattern is especially common in the minutes after sign-in, when startup
/// apps are still initialising: browsers cache-warm, sync clients index,
/// launchers check for updates — all simultaneously.
///
/// Thresholds:
///   High-impact startup apps : ≥ 2 entries in the startup list
///   CPU pressure             : ≥ 55 % average over the last minute, OR
///   Disk I/O pressure        : combined read+write ≥ 8 MB/s
///
/// The combined signal gives ~80 % confidence: the telemetry confirms the system
/// is under load, the startup list explains a plausible cause.
///
/// This rule intentionally does NOT fire when only one high-impact app is
/// present — a single app alone is already covered by HighImpactStartupAppsRule.
/// The correlated insight is only raised when the configuration + telemetry
/// together paint a clear picture.
///
/// Severity: Warning (live impact is occurring, not just potential).
/// ID: startup.resource-pressure
/// </summary>
public sealed class StartupResourceCorrelationRule : IInsightRule
{
    private const int    MinHighImpactApps    = 2;
    private const double CpuPressureThreshold = 55.0;   // % average over last minute
    private const double DiskPressureMbps     = 8.0;    // MB/s combined read+write
    private const int    MinCpuSamples        = 10;     // must have ≥10 samples (~15 s)

    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.startup.open-startup-apps",
            Title:                "Review Startup Apps",
            Description:          "Open the Windows Startup Apps settings page to identify which applications are initialising in the background and selectively disable the ones you don't need right away.",
            Impact:               ActionImpact.High,
            Effort:               ActionEffort.FewMinutes,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),

        new SystemAction(
            Id:                   "action.cpu.open-task-manager",
            Title:                "Open Task Manager",
            Description:          "Sort by CPU or Disk columns to see which startup applications are currently consuming the most resources.",
            Impact:               ActionImpact.Low,
            Effort:               ActionEffort.Seconds,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),
    ];

    public string RuleId => "startup.resource-pressure";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        // Wait until the first StartupSampler refresh cycle completes.
        if (current.StartupEntries.Count == 0)
            return null;

        var highImpact = current.StartupEntries
            .Where(static e => e.Impact == StartupImpact.High)
            .ToList();

        if (highImpact.Count < MinHighImpactApps)
            return null;

        // Check for live resource pressure to confirm the correlation.
        bool cpuUnderPressure  = IsCpuUnderPressure(history);
        bool diskUnderPressure = IsDiskUnderPressure(current);

        if (!cpuUnderPressure && !diskUnderPressure)
            return null;

        // Build context strings for the detail text.
        var appNames      = BuildShortNameList(highImpact);
        var pressureDesc  = DescribePressure(cpuUnderPressure, diskUnderPressure, current, history);

        return new SystemInsight(
            Id:       RuleId,
            Severity: InsightSeverity.Warning,
            Title:    "Startup Apps Are Driving Resource Pressure",
            Detail:   $"Several high-impact startup applications ({appNames}) are configured to launch at sign-in, " +
                      $"and your system is currently {pressureDesc}. " +
                      $"This is a common pattern when startup apps are still initialising — browsers warming caches, " +
                      $"sync clients indexing files, and launchers checking for updates all compete for resources simultaneously. " +
                      $"Disabling startup entries you don't need immediately can reduce this post-boot pressure.",
            ActionHint:        "Open Task Manager to confirm which startup apps are currently active, then disable them in Startup Apps settings.",
            DetectedAt:        DateTimeOffset.Now,
            ConfidencePercent: 80,
            RecommendedActions: s_actions);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsCpuUnderPressure(ITelemetryHistoryBuffer history)
    {
        var stats = history.GetStats(TelemetryMetric.Cpu, TimeWindow.LastMinute);
        return !stats.IsEmpty
            && stats.SampleCount >= MinCpuSamples
            && stats.Average >= CpuPressureThreshold;
    }

    private static bool IsDiskUnderPressure(TelemetrySnapshot snap) =>
        snap.DiskReadMbps + snap.DiskWriteMbps >= DiskPressureMbps;

    private static string DescribePressure(
        bool              cpuUnder,
        bool              diskUnder,
        TelemetrySnapshot snap,
        ITelemetryHistoryBuffer history)
    {
        var cpuStats = history.GetStats(TelemetryMetric.Cpu, TimeWindow.LastMinute);

        if (cpuUnder && diskUnder)
            return $"experiencing elevated CPU ({cpuStats.Average:0}% avg) and disk I/O ({snap.DiskReadMbps + snap.DiskWriteMbps:0.0} MB/s)";

        if (cpuUnder)
            return $"under elevated CPU load ({cpuStats.Average:0}% average over the last minute)";

        return $"under elevated disk I/O ({snap.DiskReadMbps + snap.DiskWriteMbps:0.0} MB/s combined read/write)";
    }

    /// <summary>
    /// Builds a short comma-separated list of up to 3 high-impact app names for
    /// inline use in the detail sentence.
    /// </summary>
    private static string BuildShortNameList(List<StartupEntry> entries)
    {
        var shown    = entries.Take(3).Select(FormatName).ToList();
        int overflow = entries.Count - shown.Count;

        return overflow > 0
            ? string.Join(", ", shown) + $" and {overflow} more"
            : string.Join(", ", shown);
    }

    private static string FormatName(StartupEntry entry)
    {
        var name = entry.Name.Trim();
        return name.Length > 0 && char.IsUpper(name[0])
            ? name
            : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
    }
}
