using Lucid.Services.Analytics;
using Lucid.Services.Baseline;
using Lucid.Services.Learning;

namespace Lucid.Services.Simulation;

/// <summary>
/// Estimates the immediate and short-term impact of a simulated intervention
/// on system resource metrics.
///
/// Design principles:
///   • All estimates are bounded by historical learning data when available.
///     When no learning data exists, conservative defaults are used.
///   • Estimates are expressed as deltas (negative = improvement for CPU/RAM/Disk).
///   • No estimate claims certainty — all values are mid-points of a range.
///   • Startup impact is modelled separately from runtime impact.
///
/// Data sources used:
///   • RecommendationEffectivenessProfile — AvgCpuChange, AvgRamChange, EffectivenessRate
///   • HistoricalSummary — metric trends, health scores, data coverage
///   • MachineBaseline — machine-specific normal ranges
///   • Current telemetry — provides the starting point for delta calculations
///
/// All methods are deterministic and pure. No state, no I/O.
/// </summary>
public static class InterventionImpactEstimator
{
    // ── Action key constants ───────────────────────────────────────────────────
    private const string KeyCleanTemp        = "action.disk.clean-temp-files";
    private const string KeyCleanRecycle     = "action.disk.empty-recycle-bin";
    private const string KeyCleanWuCache     = "action.disk.clean-windows-update-cache";
    private const string KeyCleanDelivOpt   = "action.disk.clean-delivery-optimization";
    private const string KeyCleanBrowser    = "action.disk.clean-browser-cache";
    private const string KeySfc             = "action.repair.sfc-scannow";
    private const string KeyDism            = "action.repair.dism-restore-health";
    private const string KeyDisableStartup  = "action.startup.disable-startup-app";
    private const string KeyFlushDns        = "action.network.flush-dns";

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the estimated immediate impact delta for the given scenario type.
    ///
    /// Negative values for CPU/RAM/Disk deltas indicate improvement.
    /// Returns a conservative (small) estimate when insufficient learning data exists.
    /// </summary>
    public static SimulatedResourceState EstimateImpact(
        SimulationScenarioType                                          scenario,
        double                                                          currentCpu,
        double                                                          currentRam,
        double                                                          currentDisk,
        double                                                          currentThermal,
        int                                                             startupAppCount,
        int                                                             highImpactStartupCount,
        HistoricalSummary?                                              historicalSummary,
        IReadOnlyDictionary<string, RecommendationEffectivenessProfile> profiles,
        MachineBaseline?                                                baseline) =>
        scenario switch
        {
            SimulationScenarioType.DisableStartupApps =>
                EstimateStartupDisableImpact(
                    currentCpu, currentRam, currentDisk, currentThermal,
                    startupAppCount, highImpactStartupCount, profiles, baseline),

            SimulationScenarioType.CleanupDisk =>
                EstimateCleanupImpact(
                    currentCpu, currentRam, currentDisk, currentThermal,
                    historicalSummary, profiles),

            SimulationScenarioType.RepairWindows =>
                EstimateRepairImpact(
                    currentCpu, currentRam, currentDisk, currentThermal, profiles),

            SimulationScenarioType.TerminateProcess =>
                EstimateTerminateImpact(
                    currentCpu, currentRam, currentDisk, currentThermal, baseline),

            SimulationScenarioType.RestartSystem =>
                EstimateRestartImpact(
                    currentCpu, currentRam, currentDisk, currentThermal,
                    startupAppCount, highImpactStartupCount, baseline),

            SimulationScenarioType.DeferMaintenance or
            SimulationScenarioType.ContinueCurrentWorkload or
            SimulationScenarioType.StorageGrowthContinuation or
            SimulationScenarioType.WorkloadStress =>
                // These are "no-action" scenarios — the with-action branch has no delta
                NoImpact(currentCpu, currentRam, currentDisk, currentThermal),

            _ =>
                NoImpact(currentCpu, currentRam, currentDisk, currentThermal),
        };

    // ── Scenario-specific estimators ──────────────────────────────────────────

    private static SimulatedResourceState EstimateStartupDisableImpact(
        double currentCpu, double currentRam, double currentDisk, double currentThermal,
        int    appCount,   int    highImpactCount,
        IReadOnlyDictionary<string, RecommendationEffectivenessProfile> profiles,
        MachineBaseline? baseline)
    {
        // Startup apps affect boot-time behaviour, not running CPU/RAM directly.
        // The only meaningful runtime delta is a small RAM reduction for services
        // that start background processes (sync clients, launchers, etc).
        double ramDelta = -(highImpactCount * 1.8 + Math.Max(0, appCount - highImpactCount) * 0.4);

        // Clamp: never reduce RAM by more than 15% in a single action
        ramDelta = Math.Max(ramDelta, -15.0);

        // Learning override: if we have measured data, prefer it
        if (profiles.TryGetValue(KeyDisableStartup, out var profile) && profile.IsWarmEnough)
        {
            ramDelta = profile.AvgRamChange; // already negative for improvement
        }

        // Startup settling time: ForecastHelper model
        // High-impact apps: 2 min each; moderate: 0.8 min each; other: 0.2 min each
        int other = Math.Max(0, appCount - highImpactCount);
        double settlingReductionMin = highImpactCount * 2.0 + other * 0.4;
        double settlingReductionSec = Math.Min(settlingReductionMin, 12.0) * 60.0;

        // Baseline RAM normal — use as anchor for post-action state
        double baselineRam = baseline?.NormalRamMean ?? 40;
        double projectedRam = Math.Max(baselineRam, currentRam + ramDelta);

        string impact = highImpactCount > 0
            ? $"Disabling startup apps may reduce post-boot startup time by ~{settlingReductionSec:F0}s and free ~{Math.Abs(ramDelta):F0}% RAM over time."
            : $"Removing startup items may reduce post-boot settling delay by ~{settlingReductionSec:F0}s.";

        return new SimulatedResourceState(
            Cpu:                        currentCpu,
            Ram:                        projectedRam,
            Disk:                       currentDisk,
            ThermalC:                   currentThermal,
            CpuDelta:                   0,
            RamDelta:                   ramDelta,
            DiskDelta:                  0,
            ThermalDelta:               0,
            StartupSettlingSeconds:     settlingReductionSec,
            StabilityProbabilityPercent: 78,
            EstimatedReliefDays:        0,
            PrimaryImpactSummary:       impact);
    }

    private static SimulatedResourceState EstimateCleanupImpact(
        double currentCpu, double currentRam, double currentDisk, double currentThermal,
        HistoricalSummary? historicalSummary,
        IReadOnlyDictionary<string, RecommendationEffectivenessProfile> profiles)
    {
        // Combine profiles for all cleanup actions
        double diskDelta = EstimateCombinedDiskDelta(profiles);

        // If no learning data, use conservative estimate: -6% disk
        if (diskDelta == 0) diskDelta = -6.0;

        // Relief duration: based on disk trend
        double reliefDays = EstimateReliefDays(historicalSummary, Math.Abs(diskDelta));

        double projectedDisk = Math.Max(0, currentDisk + diskDelta);

        return new SimulatedResourceState(
            Cpu:                        currentCpu - 1.5,  // slight I/O relief
            Ram:                        currentRam,
            Disk:                       projectedDisk,
            ThermalC:                   currentThermal,
            CpuDelta:                   -1.5,
            RamDelta:                   0,
            DiskDelta:                  diskDelta,
            ThermalDelta:               0,
            StartupSettlingSeconds:     0,
            StabilityProbabilityPercent: 80,
            EstimatedReliefDays:        reliefDays,
            PrimaryImpactSummary:
                $"Cleanup may reclaim ~{Math.Abs(diskDelta):F0}% disk space. " +
                $"At current growth rate, storage pressure may return in approximately {FormatDays(reliefDays)}.");
    }

    private static SimulatedResourceState EstimateRepairImpact(
        double currentCpu, double currentRam, double currentDisk, double currentThermal,
        IReadOnlyDictionary<string, RecommendationEffectivenessProfile> profiles)
    {
        // Repair tools run at elevated CPU during execution — long-term state may improve
        // SFC/DISM typically take 15–45 minutes and consume significant CPU
        double cpuDuringRepair = currentCpu + 22; // temporary spike estimate

        // Post-repair: system files are healthy, may reduce background error handling
        double postRepairCpu = currentCpu - 3;
        double postRepairRam = currentRam - 2;

        bool hasSfcProfile  = profiles.TryGetValue(KeySfc, out var sfc) && sfc.IsWarmEnough;
        bool hasDismProfile = profiles.TryGetValue(KeyDism, out var dism) && dism.IsWarmEnough;

        if (hasSfcProfile)  postRepairCpu += sfc!.AvgCpuChange;
        if (hasDismProfile) postRepairCpu += dism!.AvgCpuChange;

        return new SimulatedResourceState(
            Cpu:                        Math.Clamp(postRepairCpu, 0, 100),
            Ram:                        Math.Clamp(postRepairRam, 0, 100),
            Disk:                       currentDisk,
            ThermalC:                   currentThermal,
            CpuDelta:                   postRepairCpu - currentCpu,
            RamDelta:                   -2,
            DiskDelta:                  0,
            ThermalDelta:               0,
            StartupSettlingSeconds:     0,
            StabilityProbabilityPercent: 74,
            EstimatedReliefDays:        14,
            PrimaryImpactSummary:
                "Windows repair will temporarily raise CPU during execution (~15–45 min). " +
                "Long-term system stability may improve. A restart is typically required to complete.");
    }

    private static SimulatedResourceState EstimateTerminateImpact(
        double currentCpu, double currentRam, double currentDisk, double currentThermal,
        MachineBaseline? baseline)
    {
        // Process termination gives immediate CPU/RAM relief
        // Conservative: the terminated process was contributing ~15-25% CPU if it was flagged
        double cpuDelta = -18.0;
        double ramDelta = -8.0;

        // If baseline exists, use its idle mean as the likely post-termination floor
        if (baseline is not null)
        {
            cpuDelta = -(currentCpu - baseline.IdleCpuMean) * 0.7;
            cpuDelta = Math.Max(cpuDelta, -30);
        }

        return new SimulatedResourceState(
            Cpu:                        Math.Clamp(currentCpu + cpuDelta, 0, 100),
            Ram:                        Math.Clamp(currentRam + ramDelta, 0, 100),
            Disk:                       currentDisk,
            ThermalC:                   Math.Clamp(currentThermal - 3, 0, 110),
            CpuDelta:                   cpuDelta,
            RamDelta:                   ramDelta,
            DiskDelta:                  0,
            ThermalDelta:               -3,
            StartupSettlingSeconds:     0,
            StabilityProbabilityPercent: 60, // process may auto-restart
            EstimatedReliefDays:        0,
            PrimaryImpactSummary:
                $"Terminating this process may immediately reduce CPU by ~{Math.Abs(cpuDelta):F0}% and free " +
                $"~{Math.Abs(ramDelta):F0}% RAM. Relief may be brief if the process restarts automatically.");
    }

    private static SimulatedResourceState EstimateRestartImpact(
        double currentCpu, double currentRam, double currentDisk, double currentThermal,
        int    startupAppCount, int highImpactCount,
        MachineBaseline? baseline)
    {
        // Restart clears RAM fragmentation to a baseline clean state
        double cleanRam  = baseline?.NormalRamMean ?? 35.0;
        double ramDelta  = cleanRam - currentRam; // usually negative = improvement

        // CPU returns to idle after boot settling
        double cleanCpu  = baseline?.IdleCpuMean ?? 20.0;
        double cpuDelta  = cleanCpu - currentCpu;

        // Thermal reset — fans spin down, heat dissipates
        double thermalDelta = currentThermal > 65 ? -12.0 : -5.0;

        // Boot settling time (startup apps will spike CPU/RAM for 2–5 min post-login)
        int other = Math.Max(0, startupAppCount - highImpactCount);
        double settlingMin = highImpactCount * 2.0 + other * 0.5;
        double settlingSec = Math.Min(settlingMin * 60, 25.0 * 60);

        return new SimulatedResourceState(
            Cpu:                        Math.Clamp(cleanCpu, 0, 100),
            Ram:                        Math.Clamp(cleanRam, 0, 100),
            Disk:                       currentDisk, // disk unchanged
            ThermalC:                   Math.Clamp(currentThermal + thermalDelta, 0, 110),
            CpuDelta:                   cpuDelta,
            RamDelta:                   ramDelta,
            DiskDelta:                  0,
            ThermalDelta:               thermalDelta,
            StartupSettlingSeconds:     settlingSec,
            StabilityProbabilityPercent: 85,
            EstimatedReliefDays:        3, // memory fragmentation typically rebuilds over a few days
            PrimaryImpactSummary:
                $"A restart is likely to reset RAM to ~{cleanRam:F0}% (from {currentRam:F0}%) " +
                $"and reduce CPU to ~{cleanCpu:F0}%. Boot settling takes ~{settlingMin:F0} min with current startup apps.");
    }

    private static SimulatedResourceState NoImpact(
        double cpu, double ram, double disk, double thermal) =>
        new(Cpu:                        cpu,
            Ram:                        ram,
            Disk:                       disk,
            ThermalC:                   thermal,
            CpuDelta:                   0,
            RamDelta:                   0,
            DiskDelta:                  0,
            ThermalDelta:               0,
            StartupSettlingSeconds:     0,
            StabilityProbabilityPercent: 50,
            EstimatedReliefDays:        0,
            PrimaryImpactSummary:       "No intervention — current trend continues.");

    // ── Learning-aware helpers ─────────────────────────────────────────────────

    private static double EstimateCombinedDiskDelta(
        IReadOnlyDictionary<string, RecommendationEffectivenessProfile> profiles)
    {
        double total = 0;
        bool   any   = false;
        foreach (var key in new[] { KeyCleanTemp, KeyCleanRecycle, KeyCleanWuCache,
                                    KeyCleanDelivOpt, KeyCleanBrowser })
        {
            if (profiles.TryGetValue(key, out var p) && p.IsWarmEnough)
            {
                total += p.AvgDiskChange;
                any    = true;
            }
        }
        // AvgDiskChange is typically near zero since disk % rarely moves much in 5-minute
        // post-action window — fall back to structural estimate if learning is sparse
        return any && Math.Abs(total) > 1 ? total : 0;
    }

    private static double EstimateReliefDays(HistoricalSummary? summary, double reclaimedPct)
    {
        if (summary is null || summary.DiskTrend.ChangePercent <= 0)
            return 7; // no worsening trend — relief is long

        // ChangePercent is the delta over ~23 days
        double ratePerDay = summary.DiskTrend.ChangePercent / 23.0;
        if (ratePerDay <= 0) return 7;

        // Days until the reclaimed space is consumed again
        double days = reclaimedPct / ratePerDay;
        return Math.Clamp(days, 1, 30);
    }

    private static string FormatDays(double days) =>
        days < 2  ? "1–2 days" :
        days < 5  ? $"3–5 days" :
        days < 10 ? $"~{(int)days} days" :
                    $"~{(int)(days / 7)} week{((int)(days / 7) == 1 ? "" : "s")}";
}
