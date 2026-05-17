namespace ExplainMyPC.Helpers;

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
    IReadOnlyList<ProcessSample>? TopProcesses = null)
{
    /// <summary>
    /// Top processes by CPU and RAM usage for this snapshot.
    /// Always non-null — returns an empty list when process sampling has not
    /// yet produced results (first poll cycle, or ProcessSampler skipped).
    /// </summary>
    public IReadOnlyList<ProcessSample> TopProcesses { get; } = TopProcesses ?? [];
}
