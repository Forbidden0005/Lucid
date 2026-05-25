namespace Lucid.Services.Simulation;

/// <summary>
/// A pre-configured simulation scenario that the user can select with one tap.
///
/// Presets encode a meaningful scenario name, a descriptive subtitle,
/// an icon glyph, the scenario type, and the recommended forecast horizon.
/// </summary>
public sealed record SimulationPreset(
    string                 Name,
    string                 Description,
    string                 Glyph,
    SimulationScenarioType ScenarioType,
    SimulationHorizon      Horizon,
    int                    HorizonIndex);

/// <summary>
/// Curated library of pre-configured simulation presets.
///
/// Presets cover the most common questions users ask about their machine:
///   "What if I restart?", "What if disk fills up?", "What if I do nothing?"
///
/// Each preset pairs a scenario with the most meaningful forecast horizon
/// for that class of intervention — e.g. disk growth is measured in days,
/// not minutes; a restart's benefit is visible within an hour.
///
/// The preset list is intentionally concise (6 items) so it fits in a
/// single glanceable row. Complex customization lives in the full scenario
/// builder below.
/// </summary>
public static class SimulationPresetLibrary
{
    /// <summary>All available quick-pick presets, ordered for display.</summary>
    public static IReadOnlyList<SimulationPreset> All { get; } =
    [
        new SimulationPreset(
            Name:         "Quick Restart",
            Description:  "RAM recovery & CPU reset",
            Glyph:        "",   // Sync / Refresh
            ScenarioType: SimulationScenarioType.RestartSystem,
            Horizon:      SimulationHorizon.OneHour,
            HorizonIndex: 1),

        new SimulationPreset(
            Name:         "Startup Bloat",
            Description:  "Disable high-impact startup apps",
            Glyph:        "",   // Launch
            ScenarioType: SimulationScenarioType.DisableStartupApps,
            Horizon:      SimulationHorizon.FourHours,
            HorizonIndex: 2),

        new SimulationPreset(
            Name:         "Low Disk Space",
            Description:  "Cleanup temp & cache files",
            Glyph:        "",   // Storage
            ScenarioType: SimulationScenarioType.CleanupDisk,
            Horizon:      SimulationHorizon.TwentyFourHours,
            HorizonIndex: 3),

        new SimulationPreset(
            Name:         "System Repair",
            Description:  "Windows integrity check (SFC/DISM)",
            Glyph:        "",   // Repair / Wrench
            ScenarioType: SimulationScenarioType.RepairWindows,
            Horizon:      SimulationHorizon.FourHours,
            HorizonIndex: 2),

        new SimulationPreset(
            Name:         "Kill a Process",
            Description:  "Terminate a resource-heavy app",
            Glyph:        "",   // Delete
            ScenarioType: SimulationScenarioType.TerminateProcess,
            Horizon:      SimulationHorizon.OneHour,
            HorizonIndex: 1),

        new SimulationPreset(
            Name:         "Do Nothing",
            Description:  "If maintenance is skipped",
            Glyph:        "",   // Clock / Timeline
            ScenarioType: SimulationScenarioType.DeferMaintenance,
            Horizon:      SimulationHorizon.SevenDays,
            HorizonIndex: 4),
    ];
}
