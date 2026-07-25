using FluentAssertions;
using Lucid.Helpers;
using Lucid.Services.Startup;
using Xunit;

namespace Lucid.Tests.Intelligence;

/// <summary>
/// Guards the enabled/disabled split that keeps disabled startup apps from
/// being flagged as congestion. The field bug: a machine with Steam, Discord,
/// Epic, BlueStacks, etc. all DISABLED still tripped the "Heavy Apps Load at
/// Every Startup" warning because the rules counted raw StartupEntries. Rules
/// now read EnabledStartupEntries, which this suite pins down.
/// </summary>
public sealed class StartupEnabledFilterTests
{
    private static StartupEntry Entry(string name, StartupImpact impact, bool enabled) =>
        new(name, $@"C:\Apps\{name}.exe", name.ToLowerInvariant(),
            StartupLocation.HkcuRun, impact, enabled);

    private static TelemetrySnapshot Snap(params StartupEntry[] entries) =>
        new(CpuPercent: 0, CpuFrequencyGhz: 0, CpuCoreCount: 0,
            RamPercent: 0, RamUsedGb: 0, RamTotalGb: 0,
            DiskPercent: 0, DiskUsedGb: 0, DiskTotalGb: 0,
            GpuPercent: 0, GpuAvailable: false,
            StartupEntries: entries);

    /// <summary>Disabled entries are excluded from the enabled subset; only the
    /// one enabled app survives.</summary>
    [Fact]
    public void EnabledStartupEntries_ExcludesDisabled()
    {
        // Mirrors the real machine: one enabled Chrome, everything else off.
        var snap = Snap(
            Entry("Chrome",  StartupImpact.High, enabled: true),
            Entry("Steam",   StartupImpact.High, enabled: false),
            Entry("Discord", StartupImpact.High, enabled: false),
            Entry("Epic",    StartupImpact.High, enabled: false));

        snap.EnabledStartupEntries.Should().ContainSingle()
            .Which.Name.Should().Be("Chrome");
    }

    /// <summary>A fully switched-off startup list yields zero enabled entries.</summary>
    [Fact]
    public void EnabledStartupEntries_AllDisabled_IsEmpty()
    {
        var snap = Snap(
            Entry("Steam",   StartupImpact.High, enabled: false),
            Entry("Discord", StartupImpact.High, enabled: false));

        snap.EnabledStartupEntries.Should().BeEmpty(
            "a fully switched-off startup list must read as zero load");
    }

    /// <summary>The raw list keeps disabled entries (for the Repairs UI) while
    /// the enabled view filters them out.</summary>
    [Fact]
    public void StartupEntries_RetainsDisabled_SoRepairsUiCanReEnable()
    {
        var snap = Snap(
            Entry("Chrome", StartupImpact.High, enabled: true),
            Entry("Steam",  StartupImpact.High, enabled: false));

        // The raw list keeps disabled entries; only the Enabled view filters.
        snap.StartupEntries.Should().HaveCount(2);
        snap.EnabledStartupEntries.Should().ContainSingle();
    }
}
