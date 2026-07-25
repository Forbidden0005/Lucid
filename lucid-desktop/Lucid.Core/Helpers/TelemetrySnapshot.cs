using Lucid.Services.Startup;

namespace Lucid.Helpers;

/// <summary>
/// Immutable snapshot of all hardware telemetry captured in a single poll cycle.
///
/// Core fields (Phase 2) — provided by TelemetryPoller and WindowsTelemetryService:
///   CPU, RAM, GPU, Disk percentages, sizes, and GPU availability.
///
/// Extended fields (Phase 3) — provided by WindowsTelemetryService only.
///   These default to zero / false so the Phase 2 TelemetryPoller can still
///   construct snapshots using named arguments without specifying them.
///
///   GpuVramUsedGb / GpuVramTotalGb  — from "GPU Adapter Memory" perf counter
///   DiskReadMbps  / DiskWriteMbps   — from "PhysicalDisk" perf counter
///   CpuTemperatureCelsius           — from "Thermal Zone Information" counter
///   CpuTemperatureAvailable         — false when ACPI zones are not exposed
///
/// Process fields (Phase 4) — populated every 3 ticks (~4.5 s) by ProcessSampler.
///   TopProcesses — union of top-N by CPU and top-N by RAM, deduplicated by PID.
///   Used by insight rules for root-cause attribution ("Chrome is using 8 GB RAM").
///   First two ticks after startup always return an empty list (delta warm-up).
///
/// Startup fields (Phase 5) — populated every 40 ticks (~60 s) by StartupSampler.
///   StartupEntries — every startup app from Run registry keys and the Startup
///   folder, BOTH enabled and disabled (each entry carries its own IsEnabled
///   flag, resolved from the Windows StartupApproved state). Insight rules that
///   reason about startup load must use EnabledStartupEntries, not this raw list.
///   Refreshed infrequently because the startup list changes rarely at runtime.
///   Null until the first refresh cycle completes; rules must guard against null.
/// </summary>
public sealed record TelemetrySnapshot(
    // ── CPU ───────────────────────────────────────────────────────────────────
    double CpuPercent,
    double CpuFrequencyGhz,
    int    CpuCoreCount,

    // ── RAM ───────────────────────────────────────────────────────────────────
    double RamPercent,
    double RamUsedGb,
    double RamTotalGb,

    // ── Disk ──────────────────────────────────────────────────────────────────
    double DiskPercent,
    double DiskUsedGb,
    double DiskTotalGb,

    // ── GPU ───────────────────────────────────────────────────────────────────
    double GpuPercent,
    bool   GpuAvailable,

    // ── Extended (Phase 3) — callers that provide only the Phase 2 fields
    //    can omit these; they default to "not available".
    double GpuVramUsedGb           = 0,
    double GpuVramTotalGb          = 0,
    double DiskReadMbps            = 0,
    double DiskWriteMbps           = 0,
    double CpuTemperatureCelsius   = 0,
    bool   CpuTemperatureAvailable = false,

    // ── Process samples (Phase 4) — null on first poll cycle; empty list
    //    thereafter until ProcessSampler produces its first delta sample.
    //    Callers always see a non-null list via the record body override below.
    IReadOnlyList<ProcessSample>? TopProcesses = null,

    // ── Startup entries (Phase 5) — null until the first StartupSampler refresh
    //    cycle completes (~60 s after app start). Insight rules that consume
    //    this field must guard against null with an early return.
    IReadOnlyList<StartupEntry>? StartupEntries = null)
{
    /// <summary>
    /// Top processes by CPU and RAM usage for this snapshot.
    /// Always non-null — returns an empty list when process sampling has not
    /// yet produced results (first poll cycle, or ProcessSampler skipped).
    /// </summary>
    public IReadOnlyList<ProcessSample> TopProcesses { get; } = TopProcesses ?? [];

    /// <summary>
    /// Every startup entry visible to the current user — enabled AND disabled.
    /// Disabled entries are retained so the Repairs UI can show and re-enable
    /// them. Always non-null — returns an empty list until the first
    /// StartupSampler refresh cycle completes (approximately 60 s after start).
    /// </summary>
    public IReadOnlyList<StartupEntry> StartupEntries { get; } = StartupEntries ?? [];

    /// <summary>
    /// The startup entries the user actually has ENABLED — the subset that
    /// really launches at sign-in. Insight rules reasoning about startup load
    /// must use this, never <see cref="StartupEntries"/> (which also carries
    /// disabled entries), so a machine whose heavy apps are all switched off is
    /// never flagged as congested.
    /// </summary>
    public IReadOnlyList<StartupEntry> EnabledStartupEntries =>
        StartupEntries.Where(static e => e.IsEnabled).ToList();
}
