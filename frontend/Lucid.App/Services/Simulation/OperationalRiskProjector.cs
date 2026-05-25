using Lucid.Services.Analytics;
using Lucid.Services.Learning;

namespace Lucid.Services.Simulation;

/// <summary>
/// Projects the operational risk profile for a simulated intervention.
///
/// Risk dimensions assessed:
///   • Level       — overall risk classification (Low → High)
///   • Reversibility — whether the action can be undone
///   • Recurrence  — likelihood the problem returns after intervention
///   • Rollback    — likelihood rollback will be required
///
/// Risk is shaped by three signals:
///   1. Structural knowledge about the action type (what it does)
///   2. Historical learning data (past outcomes for this action on this machine)
///   3. Current system state (e.g. elevated load increases risk of repair ops)
///
/// All methods are deterministic and pure. No state, no I/O.
/// </summary>
public static class OperationalRiskProjector
{
    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Projects the risk profile for the given scenario.
    /// </summary>
    public static OperationalRiskProjection Project(
        SimulationScenarioType                                          scenario,
        double                                                          currentCpu,
        double                                                          currentRam,
        double                                                          currentDisk,
        HistoricalSummary?                                              historicalSummary,
        IReadOnlyDictionary<string, RecommendationEffectivenessProfile> profiles) =>
        scenario switch
        {
            SimulationScenarioType.DisableStartupApps =>
                ProjectStartupRisk(historicalSummary, profiles),

            SimulationScenarioType.CleanupDisk =>
                ProjectCleanupRisk(currentDisk, historicalSummary, profiles),

            SimulationScenarioType.RepairWindows =>
                ProjectRepairRisk(currentCpu, historicalSummary, profiles),

            SimulationScenarioType.TerminateProcess =>
                ProjectTerminateRisk(historicalSummary),

            SimulationScenarioType.RestartSystem =>
                ProjectRestartRisk(currentCpu, currentRam),

            SimulationScenarioType.DeferMaintenance =>
                ProjectDeferralRisk(historicalSummary),

            SimulationScenarioType.ContinueCurrentWorkload =>
                ProjectContinuationRisk(currentCpu, currentRam),

            SimulationScenarioType.StorageGrowthContinuation =>
                ProjectStorageGrowthRisk(currentDisk, historicalSummary),

            SimulationScenarioType.WorkloadStress =>
                ProjectWorkloadStressRisk(currentCpu, currentRam),

            _ => DefaultLow(),
        };

    // ── Scenario-specific projectors ──────────────────────────────────────────

    private static OperationalRiskProjection ProjectStartupRisk(
        HistoricalSummary? summary,
        IReadOnlyDictionary<string, RecommendationEffectivenessProfile> profiles)
    {
        bool hasLearning = profiles.TryGetValue("action.startup.disable-startup-app", out var p)
                           && p is { IsWarmEnough: true };

        // Historical worsened outcomes raise risk level
        int worsenedPct = hasLearning ? (int)(p!.WorsenedCount * 100.0 / Math.Max(1, p.TotalAnalyzed)) : 10;
        bool isElevated = worsenedPct > 25;

        return new OperationalRiskProjection(
            Level:                       isElevated ? ProjectedRiskLevel.Moderate : ProjectedRiskLevel.Low,
            Label:                       isElevated ? "Moderate" : "Low",
            IsReversible:                true,
            RecurrenceLikelihoodPercent: 15, // startup items are persistent; once removed, stay removed
            RollbackLikelihoodPercent:   hasLearning ? worsenedPct : 8,
            RiskFactors:
            [
                "Disabling required background services may reduce system responsiveness.",
                "Some startup apps are dependencies for other software.",
            ],
            Mitigations:
            [
                "Only disable apps confirmed unnecessary for your workflow.",
                "A backup of the current startup state is created automatically before changes.",
                "Re-enabling a startup app is immediate — no restart required.",
            ]);
    }

    private static OperationalRiskProjection ProjectCleanupRisk(
        double             currentDisk,
        HistoricalSummary? summary,
        IReadOnlyDictionary<string, RecommendationEffectivenessProfile> profiles)
    {
        // High disk pressure makes cleanup more urgent but also increases deletion risk
        bool highPressure = currentDisk > 88;

        return new OperationalRiskProjection(
            Level:                       ProjectedRiskLevel.Low,
            Label:                       "Low",
            IsReversible:                false, // temp files are deleted
            RecurrenceLikelihoodPercent: 70,    // disk fills up again
            RollbackLikelihoodPercent:   5,
            RiskFactors:
            [
                "Deleted temporary files cannot be recovered (they are not moved to Recycle Bin).",
                highPressure
                    ? "Disk is near capacity — cleanup is time-sensitive."
                    : "Disk usage is moderate — cleanup is precautionary.",
            ],
            Mitigations:
            [
                "Only safe-to-delete cached and temporary files are targeted.",
                "User documents, downloads, and program data are never touched.",
                "A dry-run preview is available before any files are deleted.",
            ]);
    }

    private static OperationalRiskProjection ProjectRepairRisk(
        double             currentCpu,
        HistoricalSummary? summary,
        IReadOnlyDictionary<string, RecommendationEffectivenessProfile> profiles)
    {
        bool underLoad = currentCpu > 70;

        return new OperationalRiskProjection(
            Level:                       underLoad ? ProjectedRiskLevel.Moderate : ProjectedRiskLevel.Low,
            Label:                       underLoad ? "Moderate" : "Low",
            IsReversible:                false, // system repairs are one-way by nature
            RecurrenceLikelihoodPercent: 20,
            RollbackLikelihoodPercent:   5,
            RiskFactors:
            [
                "SFC and DISM consume significant CPU and I/O during execution (15–45 minutes).",
                "A system restart is required to complete some repairs.",
                underLoad
                    ? "Current CPU load is elevated — repair will compound system pressure during execution."
                    : "System load is reasonable for repair operations.",
            ],
            Mitigations:
            [
                "Schedule repairs when the machine is idle or overnight.",
                "Save all work before starting — a restart may be required.",
                "Repair tools are built into Windows and are fully safe to run.",
            ]);
    }

    private static OperationalRiskProjection ProjectTerminateRisk(HistoricalSummary? summary)
    {
        return new OperationalRiskProjection(
            Level:                       ProjectedRiskLevel.Moderate,
            Label:                       "Moderate",
            IsReversible:                false, // process state is lost
            RecurrenceLikelihoodPercent: 65,    // many processes auto-restart
            RollbackLikelihoodPercent:   0,
            RiskFactors:
            [
                "Terminating a process loses its current state (unsaved work may be lost).",
                "Many system services and background processes restart automatically.",
                "The underlying cause of high resource usage is not addressed by termination.",
            ],
            Mitigations:
            [
                "Only terminate processes you understand or that have been flagged by Lucid.",
                "Close the process using its own exit option first — termination is a last resort.",
                "Address the root cause (background process, startup item, or driver) for lasting relief.",
            ]);
    }

    private static OperationalRiskProjection ProjectRestartRisk(double currentCpu, double currentRam)
    {
        bool urgentRam = currentRam > 88;

        return new OperationalRiskProjection(
            Level:                       ProjectedRiskLevel.Low,
            Label:                       "Low",
            IsReversible:                false, // session state is lost
            RecurrenceLikelihoodPercent: 30,    // issues often return after a few hours/days
            RollbackLikelihoodPercent:   0,
            RiskFactors:
            [
                "All unsaved work in open applications will be lost.",
                "Applications will need to be reopened after restart.",
                urgentRam
                    ? "RAM is critically pressured — a restart is strongly indicated."
                    : "Current session state should be saved before restarting.",
            ],
            Mitigations:
            [
                "Save all open documents before restarting.",
                "Review startup apps to ensure only needed applications load at boot.",
                "If issues return quickly after restart, the root cause is likely a startup service.",
            ]);
    }

    private static OperationalRiskProjection ProjectDeferralRisk(HistoricalSummary? summary)
    {
        bool hasPatterns = summary?.RecurringPatterns.Count > 0;
        bool isEscalating = summary?.RecurringPatterns.Any(p => p.IsEscalating) ?? false;

        ProjectedRiskLevel level = isEscalating ? ProjectedRiskLevel.Elevated
                                 : hasPatterns   ? ProjectedRiskLevel.Moderate
                                 :                 ProjectedRiskLevel.Low;

        return new OperationalRiskProjection(
            Level:                       level,
            Label:                       level.ToString(),
            IsReversible:                true,
            RecurrenceLikelihoodPercent: 90,
            RollbackLikelihoodPercent:   0,
            RiskFactors:
            [
                isEscalating
                    ? "One or more issues are escalating in frequency — deferral increases accumulation."
                    : "Deferring maintenance allows issues to accumulate over time.",
                hasPatterns
                    ? $"{summary!.RecurringPatterns.Count} recurring pattern(s) will likely continue without intervention."
                    : "Current issues may worsen without attention.",
            ],
            Mitigations:
            [
                "Review the Insights page to understand which issues are most pressing.",
                "Schedule a maintenance window during an idle period.",
                "Use the Watchtower page to monitor trend direction.",
            ]);
    }

    private static OperationalRiskProjection ProjectContinuationRisk(double cpu, double ram)
    {
        bool highCpu = cpu > 75;
        bool highRam = ram > 80;
        ProjectedRiskLevel level = (highCpu && highRam) ? ProjectedRiskLevel.Elevated
                                 : (highCpu || highRam) ? ProjectedRiskLevel.Moderate
                                 :                        ProjectedRiskLevel.Low;

        return new OperationalRiskProjection(
            Level:                       level,
            Label:                       level.ToString(),
            IsReversible:                true,
            RecurrenceLikelihoodPercent: 85,
            RollbackLikelihoodPercent:   0,
            RiskFactors:
            [
                highCpu ? $"CPU is at {cpu:F0}% — continuation may cause thermal stress." : "CPU load is manageable.",
                highRam ? $"RAM at {ram:F0}% — continued pressure may trigger paging." : "RAM pressure is within bounds.",
            ],
            Mitigations:
            [
                "Close unused applications to reduce memory pressure.",
                "Review Processes page for unexpectedly high CPU consumers.",
                "Consider scheduling intensive tasks during off-peak hours.",
            ]);
    }

    private static OperationalRiskProjection ProjectStorageGrowthRisk(
        double currentDisk, HistoricalSummary? summary)
    {
        bool critical = currentDisk > 88;
        bool worsening = summary?.DiskTrend.Direction == TrendDirection.Worsening;

        ProjectedRiskLevel level = critical ? ProjectedRiskLevel.High
                                 : worsening ? ProjectedRiskLevel.Elevated
                                 :             ProjectedRiskLevel.Moderate;

        return new OperationalRiskProjection(
            Level:                       level,
            Label:                       level.ToString(),
            IsReversible:                true,
            RecurrenceLikelihoodPercent: 95,
            RollbackLikelihoodPercent:   0,
            RiskFactors: new string[]
            {
                critical ? "Disk is near capacity — system performance degrades severely above 90%." : "",
                worsening ? "Disk usage trend is worsening." : "Disk trend is stable or improving.",
            }.Where(s => s.Length > 0).ToArray(),
            Mitigations:
            [
                "Run disk cleanup to reclaim temporary and cached space.",
                "Review the Storage page to identify large files and duplicates.",
                "Consider archiving infrequently accessed files.",
            ]);
    }

    private static OperationalRiskProjection ProjectWorkloadStressRisk(double cpu, double ram)
    {
        bool thermal = cpu > 70;

        return new OperationalRiskProjection(
            Level:                       thermal ? ProjectedRiskLevel.Elevated : ProjectedRiskLevel.Moderate,
            Label:                       thermal ? "Elevated" : "Moderate",
            IsReversible:                true,
            RecurrenceLikelihoodPercent: 70,
            RollbackLikelihoodPercent:   0,
            RiskFactors: new string[]
            {
                thermal ? "Sustained high CPU workloads risk thermal throttling." : "Workload is within thermal bounds.",
                ram > 70 ? $"Memory pressure ({ram:F0}%) may limit workload performance." : "",
            }.Where(s => s.Length > 0).ToArray(),
            Mitigations:
            [
                "Ensure adequate airflow and system cooling.",
                "Break intensive tasks into smaller sessions with cooling intervals.",
                "Monitor temperature on the Dashboard during heavy workloads.",
            ]);
    }

    private static OperationalRiskProjection DefaultLow() =>
        new(Level:                       ProjectedRiskLevel.Low,
            Label:                       "Low",
            IsReversible:                true,
            RecurrenceLikelihoodPercent: 30,
            RollbackLikelihoodPercent:   5,
            RiskFactors:                 ["No significant risk factors identified for this scenario."],
            Mitigations:                 ["Standard operating precautions apply."]);
}
