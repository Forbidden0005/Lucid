using FluentAssertions;
using Lucid.Helpers;
using Lucid.Services.Governance;
using Xunit;

namespace Lucid.Tests.Governance;

/// <summary>
/// Verifies the pure pressure analyzer: each threshold sets exactly its reason
/// flag, and mode precedence resolves combined reasons to the highest-priority
/// mode (Thermal > Gaming > LowPower > HighLoad > Normal).
/// </summary>
public sealed class RuntimePressureAnalyzerTests
{
    /// <summary>Calm-system snapshot; override only the field under test.</summary>
    private static TelemetrySnapshot Snapshot(
        double cpuPercent = 10,
        double gpuPercent = 5,
        bool   gpuAvailable = true,
        double diskPercent = 20,
        double cpuTempC = 40,
        bool   cpuTempAvailable = true)
        => new(
            CpuPercent:              cpuPercent,
            CpuFrequencyGhz:         3.5,
            CpuCoreCount:            8,
            RamPercent:              40,
            RamUsedGb:               12.8,
            RamTotalGb:              32,
            DiskPercent:             diskPercent,
            DiskUsedGb:              200,
            DiskTotalGb:             1000,
            GpuPercent:              gpuPercent,
            GpuAvailable:            gpuAvailable,
            CpuTemperatureCelsius:   cpuTempC,
            CpuTemperatureAvailable: cpuTempAvailable);

    // ── Individual thresholds ─────────────────────────────────────────────────

    [Fact]
    public void CalmSystem_YieldsNormalWithNoReasons()
    {
        var (mode, reasons) = RuntimePressureAnalyzer.Analyze(Snapshot(), isOnBattery: false);

        mode.Should().Be(RuntimeMode.Normal);
        reasons.Should().Be(ThrottlingReason.None);
    }

    [Theory]
    [InlineData(75.0, true)]   // at threshold — flagged
    [InlineData(74.9, false)]  // just below — not flagged
    public void CpuThreshold_At75Percent(double cpu, bool expectPressure)
    {
        var (mode, reasons) = RuntimePressureAnalyzer.Analyze(Snapshot(cpuPercent: cpu), isOnBattery: false);

        reasons.HasFlag(ThrottlingReason.CpuPressure).Should().Be(expectPressure);
        mode.Should().Be(expectPressure ? RuntimeMode.HighLoad : RuntimeMode.Normal);
    }

    [Theory]
    [InlineData(68.0, 25.0, true, true)]    // both at threshold — gaming
    [InlineData(67.9, 25.0, true, false)]   // GPU just below — not gaming
    [InlineData(68.0, 24.9, true, false)]   // CPU below floor — not gaming
    [InlineData(90.0, 50.0, false, false)]  // no GPU counter — never gaming
    public void GamingHeuristic_RequiresHighGpuAndCpuFloorAndGpuCounter(
        double gpu, double cpu, bool gpuAvailable, bool expectGaming)
    {
        var (mode, reasons) = RuntimePressureAnalyzer.Analyze(
            Snapshot(cpuPercent: cpu, gpuPercent: gpu, gpuAvailable: gpuAvailable),
            isOnBattery: false);

        reasons.HasFlag(ThrottlingReason.GamingDetected).Should().Be(expectGaming);
        if (expectGaming)
            mode.Should().Be(RuntimeMode.Gaming);
    }

    [Theory]
    [InlineData(87.0, true, true)]    // at threshold, counter available — thermal
    [InlineData(86.9, true, false)]   // just below — not thermal
    [InlineData(95.0, false, false)]  // hot but no ACPI counter — must not trigger
    public void ThermalThreshold_At87CelsiusWhenCounterAvailable(
        double tempC, bool available, bool expectThermal)
    {
        var (mode, reasons) = RuntimePressureAnalyzer.Analyze(
            Snapshot(cpuTempC: tempC, cpuTempAvailable: available),
            isOnBattery: false);

        reasons.HasFlag(ThrottlingReason.ThermalStress).Should().Be(expectThermal);
        if (expectThermal)
            mode.Should().Be(RuntimeMode.ThermalProtection);
    }

    [Fact]
    public void OnBattery_SetsBatteryModeAndLowPower()
    {
        var (mode, reasons) = RuntimePressureAnalyzer.Analyze(Snapshot(), isOnBattery: true);

        reasons.Should().Be(ThrottlingReason.BatteryMode);
        mode.Should().Be(RuntimeMode.LowPower);
    }

    /// <summary>
    /// Pins a finding from the 2026-07-25 governance audit: DiskPressure sets
    /// its reason flag but never influences the runtime mode — a disk-saturated
    /// system stays in Normal mode. This is currently observability-only by
    /// design (no mode maps to disk pressure). If disk pressure should ever
    /// throttle background work, that is a deliberate policy change, not a fix.
    /// </summary>
    [Theory]
    [InlineData(85.0, true)]
    [InlineData(84.9, false)]
    public void DiskPressure_SetsFlagButNeverChangesMode(double disk, bool expectFlag)
    {
        var (mode, reasons) = RuntimePressureAnalyzer.Analyze(
            Snapshot(diskPercent: disk), isOnBattery: false);

        reasons.HasFlag(ThrottlingReason.DiskPressure).Should().Be(expectFlag);
        mode.Should().Be(RuntimeMode.Normal, "disk pressure is observability-only today");
    }

    // ── Mode precedence ───────────────────────────────────────────────────────

    [Fact]
    public void AllReasonsActive_ThermalWins()
    {
        var (mode, reasons) = RuntimePressureAnalyzer.Analyze(
            Snapshot(cpuPercent: 90, gpuPercent: 80, diskPercent: 95, cpuTempC: 92),
            isOnBattery: true);

        reasons.Should().Be(
            ThrottlingReason.ThermalStress | ThrottlingReason.GamingDetected |
            ThrottlingReason.BatteryMode   | ThrottlingReason.CpuPressure    |
            ThrottlingReason.DiskPressure);
        mode.Should().Be(RuntimeMode.ThermalProtection);
    }

    [Fact]
    public void GamingPlusBatteryPlusCpu_GamingWins()
    {
        var (mode, _) = RuntimePressureAnalyzer.Analyze(
            Snapshot(cpuPercent: 90, gpuPercent: 80), isOnBattery: true);

        mode.Should().Be(RuntimeMode.Gaming);
    }

    [Fact]
    public void BatteryPlusCpu_LowPowerWins()
    {
        var (mode, _) = RuntimePressureAnalyzer.Analyze(
            Snapshot(cpuPercent: 90), isOnBattery: true);

        mode.Should().Be(RuntimeMode.LowPower);
    }

    // ── Reason / mode text ────────────────────────────────────────────────────

    [Fact]
    public void DescribeReasons_None_ReportsNoThrottling()
    {
        RuntimePressureAnalyzer.DescribeReasons(ThrottlingReason.None)
            .Should().Be("No throttling conditions detected.");
    }

    [Fact]
    public void DescribeReasons_SingleReason_NoConjunction()
    {
        RuntimePressureAnalyzer.DescribeReasons(ThrottlingReason.BatteryMode)
            .Should().Be("Due to battery power.");
    }

    [Fact]
    public void DescribeReasons_MultipleReasons_JoinedWithAnd()
    {
        var text = RuntimePressureAnalyzer.DescribeReasons(
            ThrottlingReason.CpuPressure | ThrottlingReason.ThermalStress | ThrottlingReason.BatteryMode);

        text.Should().Be("Due to elevated CPU temperature, battery power and high CPU load.");
    }

    [Fact]
    public void DescribeMode_ReturnsNonEmptyLabelForEveryMode()
    {
        foreach (var mode in Enum.GetValues<RuntimeMode>())
        {
            RuntimePressureAnalyzer.DescribeMode(mode).Should().NotBeNullOrWhiteSpace();
        }
    }
}
