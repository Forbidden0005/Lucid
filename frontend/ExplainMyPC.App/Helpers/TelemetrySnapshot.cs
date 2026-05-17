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
    bool   CpuTemperatureAvailable = false);
