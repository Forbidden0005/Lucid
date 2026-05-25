using Lucid.Helpers;

namespace Lucid.Services.Governance;

/// <summary>
/// Pure static analyzer that derives <see cref="RuntimeMode"/> and
/// <see cref="ThrottlingReason"/> from a live <see cref="TelemetrySnapshot"/>.
///
/// All thresholds are conservative — the goal is to protect responsiveness
/// and thermal headroom, not to optimise benchmark scores.
///
/// Heuristics:
///   Gaming      — GPU ≥ GamingGpuPct AND CPU ≥ GamingCpuFloor.
///                 A dedicated GPU running at high utilisation while the CPU
///                 is also active strongly suggests a game or GPU workload.
///   Thermal     — CPU temperature ≥ ThermalCriticalC when available.
///   Battery     — externally supplied (PowerManager query).
///   CPU pressure — CPU ≥ CpuHighPct for a rolling-average sample.
///   Disk        — Disk activity ≥ DiskHighPct.
///
/// Mode priority (highest wins):
///   ThermalProtection > Gaming > LowPower > HighLoad > Normal
/// </summary>
internal static class RuntimePressureAnalyzer
{
    // ── Thresholds ─────────────────────────────────────────────────────────────

    /// <summary>CPU percent above which "high load" is flagged.</summary>
    private const double CpuHighPct          = 75.0;

    /// <summary>GPU percent above which gaming is suspected.</summary>
    private const double GamingGpuPct        = 68.0;

    /// <summary>Minimum CPU percent required alongside high GPU to infer gaming.</summary>
    private const double GamingCpuFloor      = 25.0;

    /// <summary>CPU temperature (°C) above which ThermalProtection activates.</summary>
    private const double ThermalCriticalC    = 87.0;

    /// <summary>Disk percent above which disk pressure is flagged.</summary>
    private const double DiskHighPct         = 85.0;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Analyzes the telemetry snapshot and returns the inferred runtime mode
    /// and bitmask of throttling reasons.
    /// </summary>
    /// <param name="snapshot">Most recent telemetry reading.</param>
    /// <param name="isOnBattery">
    ///   True when the system is running on battery (caller is responsible for
    ///   checking PowerManager — avoids a WinRT dependency in this pure analyzer).
    /// </param>
    public static (RuntimeMode Mode, ThrottlingReason Reasons) Analyze(
        TelemetrySnapshot snapshot,
        bool              isOnBattery)
    {
        var reasons = ThrottlingReason.None;

        if (isOnBattery)
            reasons |= ThrottlingReason.BatteryMode;

        if (snapshot.CpuPercent >= CpuHighPct)
            reasons |= ThrottlingReason.CpuPressure;

        if (snapshot.CpuTemperatureAvailable
            && snapshot.CpuTemperatureCelsius >= ThermalCriticalC)
            reasons |= ThrottlingReason.ThermalStress;

        if (snapshot.GpuAvailable
            && snapshot.GpuPercent >= GamingGpuPct
            && snapshot.CpuPercent >= GamingCpuFloor)
            reasons |= ThrottlingReason.GamingDetected;

        if (snapshot.DiskPercent >= DiskHighPct)
            reasons |= ThrottlingReason.DiskPressure;

        // Mode: highest-priority reason wins
        RuntimeMode mode = reasons switch
        {
            _ when (reasons & ThrottlingReason.ThermalStress)  != 0 => RuntimeMode.ThermalProtection,
            _ when (reasons & ThrottlingReason.GamingDetected) != 0 => RuntimeMode.Gaming,
            _ when (reasons & ThrottlingReason.BatteryMode)    != 0 => RuntimeMode.LowPower,
            _ when (reasons & ThrottlingReason.CpuPressure)    != 0 => RuntimeMode.HighLoad,
            _                                                        => RuntimeMode.Normal,
        };

        return (mode, reasons);
    }

    // ── Reason text ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a human-readable explanation of the current throttling reasons.
    /// Used in narrative summaries and the governance UI.
    /// </summary>
    public static string DescribeReasons(ThrottlingReason reasons)
    {
        if (reasons == ThrottlingReason.None)
            return "No throttling conditions detected.";

        var parts = new List<string>(4);
        if ((reasons & ThrottlingReason.ThermalStress)  != 0) parts.Add("elevated CPU temperature");
        if ((reasons & ThrottlingReason.GamingDetected) != 0) parts.Add("high GPU activity");
        if ((reasons & ThrottlingReason.BatteryMode)    != 0) parts.Add("battery power");
        if ((reasons & ThrottlingReason.CpuPressure)    != 0) parts.Add("high CPU load");
        if ((reasons & ThrottlingReason.DiskPressure)   != 0) parts.Add("high disk activity");

        return parts.Count == 1
            ? $"Due to {parts[0]}."
            : $"Due to {string.Join(", ", parts[..^1])} and {parts[^1]}.";
    }

    /// <summary>
    /// Returns a brief mode label for display in the governance page header.
    /// </summary>
    public static string DescribeMode(RuntimeMode mode) => mode switch
    {
        RuntimeMode.ThermalProtection => "Thermal Protection",
        RuntimeMode.Gaming            => "Gaming Mode",
        RuntimeMode.LowPower          => "Low Power",
        RuntimeMode.HighLoad          => "High Load",
        _                             => "Normal",
    };
}
