using Lucid.Helpers;

namespace Lucid.Services.ProcessIntel;

// ── Classification ────────────────────────────────────────────────────────────

/// <summary>Broad behavioral category for a process.</summary>
public enum ProcessCategory
{
    Browser,
    Communication,
    Game,
    ElectronApp,
    Updater,
    Service,
    DeveloperTool,
    MediaPlayer,
    CloudSync,
    Office,
    Security,
    SystemCritical,
    Runtime,
    Unknown,
}

/// <summary>Anomaly flags — multiple can be set simultaneously.</summary>
[Flags]
public enum ProcessAnomalyFlags
{
    None              = 0,
    RunawayCpu        = 1 << 0,  // sustained > 80% CPU for 3+ samples
    MemoryGrowth      = 1 << 1,  // RAM grew > 200 MB in last 60 s
    ThreadGrowth      = 1 << 2,  // thread count climbing steadily over the window
    HandleGrowth      = 1 << 3,  // handle count climbing steadily over the window
    RepeatedCrashes   = 1 << 4,  // restarted 3+ times in 5 min window
    HighRamAbsolute   = 1 << 5,  // working set > 1.5 GB
    ZombieBackground  = 1 << 6,  // > 2% CPU, no visible window, no user action
    GpuHeavy          = 1 << 7,  // heuristically identified GPU consumer
}

// ── Extended process record ───────────────────────────────────────────────────

/// <summary>
/// Extended snapshot of a single process, richer than <see cref="ProcessSample"/>.
/// Produced by <see cref="ProcessBehaviorTracker"/> once per telemetry tick.
/// </summary>
public sealed class ProcessRecord
{
    public int                ProcessId      { get; init; }
    public string             ProcessName    { get; init; } = string.Empty;
    public string             DisplayName    { get; init; } = string.Empty;
    public float              CpuPercent     { get; init; }
    public long               RamBytes       { get; init; }
    public int                ThreadCount    { get; init; }
    public int                HandleCount    { get; init; }
    public string             CommandLine    { get; init; } = string.Empty;
    public string             CompanyName    { get; init; } = string.Empty;
    public string             ExecutablePath { get; init; } = string.Empty;
    public DateTime           StartTime      { get; init; }
    public bool               HasWindow      { get; init; }
    public ProcessCategory    Category       { get; init; }
    public ProcessAnomalyFlags Anomalies     { get; init; }
    public bool               IsCritical     { get; init; }

    // Computed display helpers
    public string RamFormatted   => FormatBytes(RamBytes);
    public string CpuFormatted   => $"{CpuPercent:F1}%";
    public string UptimeFormatted
    {
        get
        {
            var age = DateTime.Now - StartTime;
            if (age.TotalDays >= 1) return $"{(int)age.TotalDays}d {age.Hours}h";
            if (age.TotalHours >= 1) return $"{(int)age.TotalHours}h {age.Minutes}m";
            return $"{(int)age.TotalMinutes}m";
        }
    }

    public bool HasAnomalies => Anomalies != ProcessAnomalyFlags.None;

    public string AnomalySummary
    {
        get
        {
            if (Anomalies == ProcessAnomalyFlags.None) return string.Empty;
            var parts = new List<string>();
            if (Anomalies.HasFlag(ProcessAnomalyFlags.RunawayCpu))      parts.Add("Runaway CPU");
            if (Anomalies.HasFlag(ProcessAnomalyFlags.MemoryGrowth))    parts.Add("Memory growing");
            if (Anomalies.HasFlag(ProcessAnomalyFlags.ThreadGrowth))     parts.Add("Threads climbing");
            if (Anomalies.HasFlag(ProcessAnomalyFlags.HandleGrowth))      parts.Add("Handles climbing");
            if (Anomalies.HasFlag(ProcessAnomalyFlags.RepeatedCrashes))  parts.Add("Repeated crashes");
            if (Anomalies.HasFlag(ProcessAnomalyFlags.HighRamAbsolute))  parts.Add("High RAM");
            if (Anomalies.HasFlag(ProcessAnomalyFlags.ZombieBackground)) parts.Add("Background hog");
            return string.Join(", ", parts);
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1_048_576     => $"{bytes / 1_024.0:F0} KB",
        < 1_073_741_824 => $"{bytes / 1_048_576.0:F1} MB",
        _               => $"{bytes / 1_073_741_824.0:F2} GB",
    };
}

/// <summary>Result published by <see cref="ProcessIntelligenceService"/> each tick.</summary>
public sealed record ProcessIntelligenceSnapshot(
    IReadOnlyList<ProcessRecord> All,
    IReadOnlyList<ProcessRecord> Anomalous,
    IReadOnlyList<ProcessRecord> TopByCpu,
    IReadOnlyList<ProcessRecord> TopByRam,
    DateTimeOffset               SnapshotAt);
