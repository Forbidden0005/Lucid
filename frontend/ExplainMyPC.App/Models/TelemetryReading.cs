namespace ExplainMyPC.Models;

/// <summary>
/// A point-in-time snapshot of system telemetry.
///
/// All percentage values are 0–100. Temperature is Celsius.
/// This is a pure data record — no business logic, no formatting.
/// The Intelligence Engine and ViewModels interpret and present these values.
/// </summary>
public sealed record TelemetryReading(
    DateTimeOffset Timestamp,

    // ── CPU ──────────────────────────────────────────────────────────────────
    double CpuPercent,
    double CpuTemperatureCelsius,
    double CpuFrequencyGhz,

    // ── Memory ───────────────────────────────────────────────────────────────
    double RamPercent,
    long   RamUsedMb,
    long   RamTotalMb,

    // ── GPU ──────────────────────────────────────────────────────────────────
    double GpuPercent,
    double GpuTemperatureCelsius,
    double GpuVramUsedGb,
    double GpuVramTotalGb,

    // ── Disk I/O ─────────────────────────────────────────────────────────────
    double DiskReadMbps,
    double DiskWriteMbps,
    double DiskUsagePercent,
    long   DiskUsedGb,
    long   DiskTotalGb
);
