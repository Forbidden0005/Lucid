namespace ExplainMyPC.Helpers;

/// <summary>
/// Immutable snapshot of system metrics captured during a single telemetry poll.
///
/// All values are already rounded — no further arithmetic needed in the ViewModel.
/// Passed from TelemetryPoller to subscribers on the UI thread.
/// </summary>
/// <param name="CpuPercent">Total CPU utilisation, 0–100.</param>
/// <param name="CpuFrequencyGhz">Average clock speed across all logical cores, in GHz.
///     0 if <c>CallNtPowerInformation</c> is unavailable on this system.</param>
/// <param name="CpuCoreCount">Logical processor count (<see cref="Environment.ProcessorCount"/>).</param>
/// <param name="RamPercent">Physical RAM utilisation, 0–100.</param>
/// <param name="RamUsedGb">Physical RAM in use, gigabytes.</param>
/// <param name="RamTotalGb">Total installed physical RAM, gigabytes.</param>
/// <param name="DiskPercent">System-drive space utilisation, 0–100.</param>
/// <param name="DiskUsedGb">System-drive used space, gigabytes.</param>
/// <param name="DiskTotalGb">System-drive total capacity, gigabytes.</param>
/// <param name="GpuPercent">Sum of GPU 3D-engine utilisation across all adapters, 0–100.
///     0 when <see cref="GpuAvailable"/> is false.</param>
/// <param name="GpuAvailable">True when the GPU Engine performance-counter category
///     exists and at least one 3D-engine instance was found.</param>
public sealed record TelemetrySnapshot(
    double CpuPercent,
    double CpuFrequencyGhz,
    int    CpuCoreCount,
    double RamPercent,
    double RamUsedGb,
    double RamTotalGb,
    double DiskPercent,
    double DiskUsedGb,
    double DiskTotalGb,
    double GpuPercent,
    bool   GpuAvailable);
