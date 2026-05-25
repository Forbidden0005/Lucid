using Lucid.Services.Analytics;
using Lucid.Services.Baseline;
using Lucid.Services.Behavior;
using Lucid.Services.Governance;
using Lucid.Services.Intelligence;
using Lucid.Services.Learning;
using Lucid.Services.Startup;

namespace Lucid.Services.Simulation;

/// <summary>
/// Orchestrates the full operational simulation pipeline.
///
/// For a given <see cref="SimulationInput"/>, the engine:
///   1. Gathers current system state (telemetry + insights + baseline)
///   2. Retrieves historical analytics and learning effectiveness profiles
///   3. Estimates the impact of the scenario intervention
///   4. Projects two trajectory branches: WithAction and WithoutAction
///   5. Scores simulation confidence from available data quality
///   6. Projects operational risk
///   7. Builds a plain-English narrative summarising the outcome comparison
///   8. Assembles and returns a complete <see cref="SimulationScenario"/>
///
/// Design invariants:
///   • Never claims certainty — all outputs carry explicit confidence scores
///   • Never auto-applies changes — simulation is always read-only
///   • Deterministic — same input state produces the same output
///   • Cancellation-aware — honours CancellationToken at each async boundary
///   • Bounded complexity — linear trajectory math only; no recursive branching
///
/// Threading:
///   SimulateAsync() is designed to be awaited from the UI thread.
///   Heavy computation runs on the thread pool via Task.Run().
/// </summary>
public sealed class OperationalSimulationEngine
{
    private readonly IHistoricalAnalyticsEngine  _history;
    private readonly IRemediationLearningService _learning;
    private readonly ITelemetryService           _telemetry;
    private readonly ISystemInsightEngine        _insights;
    private readonly ISystemBaselineService      _baseline;
    private readonly WorkloadProfilingService    _workload;
    private readonly IRuntimeGovernanceService   _governance;

    public OperationalSimulationEngine(
        IHistoricalAnalyticsEngine  history,
        IRemediationLearningService learning,
        ITelemetryService           telemetry,
        ISystemInsightEngine        insights,
        ISystemBaselineService      baseline,
        WorkloadProfilingService    workload,
        IRuntimeGovernanceService   governance)
    {
        _history    = history;
        _learning   = learning;
        _telemetry  = telemetry;
        _insights   = insights;
        _baseline   = baseline;
        _workload   = workload;
        _governance = governance;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the full simulation pipeline for the given input.
    /// Returns a complete <see cref="SimulationScenario"/> with projected branches,
    /// confidence scoring, risk projection, and narrative.
    ///
    /// Never throws — exceptions are caught and surfaced as low-confidence scenarios.
    /// </summary>
    public async Task<SimulationScenario> SimulateAsync(
        SimulationInput   input,
        CancellationToken ct = default)
    {
        try
        {
            return await Task.Run(() => RunSimulation(input, ct), ct)
                             .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return BuildCancelledScenario(input);
        }
        catch (Exception ex)
        {
            return BuildErrorScenario(input, ex.Message);
        }
    }

    // ── Core pipeline ─────────────────────────────────────────────────────────

    private SimulationScenario RunSimulation(SimulationInput input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // ── 1. Snapshot current system state ───────────────────────────────────
        var snap       = _telemetry.LastReading;
        var mlBaseline = _baseline.CurrentBaseline;

        double cpu     = snap?.CpuPercent           ?? 0;
        double ram     = snap?.RamPercent            ?? 0;
        double disk    = snap?.DiskPercent           ?? 0;
        double thermal = snap?.CpuTemperatureAvailable == true
                         ? snap.CpuTemperatureCelsius : 0;

        // Startup app composition from last telemetry reading
        int totalStartupApps     = snap?.StartupEntries?.Count ?? 0;
        int highImpactStartupApps = snap?.StartupEntries?
            .Count(e => e.Impact >= StartupImpact.High) ?? 0;

        ct.ThrowIfCancellationRequested();

        // ── 2. Historical analytics ────────────────────────────────────────────
        // Use cached last summary — ComputeAsync is too expensive to run inline.
        var summary  = _history.LastSummary;
        var profiles = _learning.GetAllProfiles();

        ct.ThrowIfCancellationRequested();

        // ── 3. Estimate intervention impact ────────────────────────────────────
        var impact = InterventionImpactEstimator.EstimateImpact(
            input.ScenarioType,
            cpu, ram, disk, thermal,
            totalStartupApps, highImpactStartupApps,
            summary, profiles, mlBaseline);

        ct.ThrowIfCancellationRequested();

        // ── 4. Project trajectories ────────────────────────────────────────────
        var cpuTrend  = summary?.CpuTrend  ?? NoTrend("CPU");
        var ramTrend  = summary?.RamTrend  ?? NoTrend("RAM");
        var diskTrend = summary?.DiskTrend ?? NoTrend("Disk");

        var withActionTrajectory = ResourceTrajectoryProjector.Project(
            cpu, ram, disk, thermal,
            cpuTrend, ramTrend, diskTrend,
            input.Horizon,
            SimulationBranchType.WithAction,
            impact);

        var withoutActionTrajectory = ResourceTrajectoryProjector.Project(
            cpu, ram, disk, thermal,
            cpuTrend, ramTrend, diskTrend,
            input.Horizon,
            SimulationBranchType.WithoutAction,
            appliedImpact: null);

        ct.ThrowIfCancellationRequested();

        // ── 5. Build projected states (end-of-horizon) ─────────────────────────
        var withActionEnd    = withActionTrajectory.Last();
        var withoutActionEnd = withoutActionTrajectory.Last();

        var withActionState = new SimulatedResourceState(
            Cpu:                        withActionEnd.Cpu,
            Ram:                        withActionEnd.Ram,
            Disk:                       withActionEnd.Disk,
            ThermalC:                   withActionEnd.ThermalC,
            CpuDelta:                   withActionEnd.Cpu  - cpu,
            RamDelta:                   withActionEnd.Ram  - ram,
            DiskDelta:                  withActionEnd.Disk - disk,
            ThermalDelta:               withActionEnd.ThermalC - thermal,
            StartupSettlingSeconds:     impact.StartupSettlingSeconds,
            StabilityProbabilityPercent: impact.StabilityProbabilityPercent,
            EstimatedReliefDays:        impact.EstimatedReliefDays,
            PrimaryImpactSummary:       impact.PrimaryImpactSummary);

        var withoutActionState = new SimulatedResourceState(
            Cpu:                        withoutActionEnd.Cpu,
            Ram:                        withoutActionEnd.Ram,
            Disk:                       withoutActionEnd.Disk,
            ThermalC:                   withoutActionEnd.ThermalC,
            CpuDelta:                   withoutActionEnd.Cpu  - cpu,
            RamDelta:                   withoutActionEnd.Ram  - ram,
            DiskDelta:                  withoutActionEnd.Disk - disk,
            ThermalDelta:               withoutActionEnd.ThermalC - thermal,
            StartupSettlingSeconds:     0,
            StabilityProbabilityPercent: ComputeNoActionStability(summary, cpu, ram, disk),
            EstimatedReliefDays:        0,
            PrimaryImpactSummary:       "Current trend continues without intervention.");

        ct.ThrowIfCancellationRequested();

        // ── 6. Confidence scoring ──────────────────────────────────────────────
        var confidence = SimulationConfidenceScorer.Score(input.ScenarioType, summary, profiles);

        // ── 7. Risk projection ─────────────────────────────────────────────────
        var risk = OperationalRiskProjector.Project(
            input.ScenarioType, cpu, ram, disk, summary, profiles);

        // ── 8. Historical basis ────────────────────────────────────────────────
        var basis = BuildHistoricalBasis(summary, profiles, input.ScenarioType);

        // ── 9. Build branches ──────────────────────────────────────────────────
        var horizonLabel = HorizonLabel(input.Horizon);

        var withAction = new SimulationBranch(
            BranchType:       SimulationBranchType.WithAction,
            Label:            "With intervention",
            Description:      $"Projected outcome if {ScenarioVerb(input.ScenarioType)} is applied now.",
            ProjectedState:   withActionState,
            Trajectory:       withActionTrajectory,
            ConfidencePercent: confidence.OverallPercent,
            NarrativeSummary: BuildWithActionNarrative(input.ScenarioType, withActionState, horizonLabel),
            KeyInsights:      BuildKeyInsights(input.ScenarioType, withActionState, risk));

        var withoutAction = new SimulationBranch(
            BranchType:       SimulationBranchType.WithoutAction,
            Label:            "Without intervention",
            Description:      $"Projected outcome over {horizonLabel} if no action is taken.",
            ProjectedState:   withoutActionState,
            Trajectory:       withoutActionTrajectory,
            ConfidencePercent: Math.Max(30, confidence.OverallPercent - 8),
            NarrativeSummary: BuildWithoutActionNarrative(input.ScenarioType, withoutActionState, horizonLabel, summary),
            KeyInsights:      BuildNoActionInsights(withoutActionState, summary));

        var branches = new List<SimulationBranch> { withAction, withoutAction };

        // ── 10. Scenario narrative ─────────────────────────────────────────────
        bool hasSufficientData = (summary?.TotalTelemetryRows ?? 0) >= 200;
        string narrative = BuildScenarioNarrative(
            input.ScenarioType, withActionState, withoutActionState,
            confidence, horizonLabel, hasSufficientData);

        return new SimulationScenario
        {
            ScenarioType     = input.ScenarioType,
            Input            = input,
            Branches         = branches,
            Confidence       = confidence,
            Risk             = risk,
            HistoricalBasis  = basis,
            NarrativeSummary = narrative,
            SimulatedAt      = DateTimeOffset.Now,
            HasSufficientData = hasSufficientData,
        };
    }

    // ── Narrative builders ────────────────────────────────────────────────────

    private static string BuildWithActionNarrative(
        SimulationScenarioType scenario,
        SimulatedResourceState state,
        string                 horizonLabel)
    {
        string cpuNote  = state.CpuDelta  < -2 ? $"CPU may decrease by ~{Math.Abs(state.CpuDelta):F0}%." : "";
        string ramNote  = state.RamDelta  < -2 ? $"RAM may decrease by ~{Math.Abs(state.RamDelta):F0}%." : "";
        string diskNote = state.DiskDelta < -1 ? $"Disk may decrease by ~{Math.Abs(state.DiskDelta):F0}%." : "";

        var parts = new[] { cpuNote, ramNote, diskNote }.Where(s => s.Length > 0).ToList();

        string metricSummary = parts.Count > 0
            ? string.Join(" ", parts)
            : "Metrics are projected to remain similar.";

        return scenario switch
        {
            SimulationScenarioType.DisableStartupApps =>
                $"Over {horizonLabel}, disabling startup apps is projected to reduce " +
                $"post-boot settling by ~{state.StartupSettlingSeconds:F0}s. {ramNote} " +
                $"Stability probability: ~{state.StabilityProbabilityPercent:F0}%.",

            SimulationScenarioType.CleanupDisk =>
                $"After cleanup, disk pressure is projected to ease. " +
                (state.EstimatedReliefDays > 0
                    ? $"At the current growth rate, storage pressure may return in ~{state.EstimatedReliefDays:F0} days. "
                    : "") +
                $"Stability probability: ~{state.StabilityProbabilityPercent:F0}%.",

            SimulationScenarioType.RestartSystem =>
                $"A restart is projected to reset RAM to ~{state.Ram:F0}% and CPU to ~{state.Cpu:F0}%. " +
                $"Boot settling will take approximately {state.StartupSettlingSeconds / 60:F0} minutes with the current startup list.",

            _ =>
                $"Over {horizonLabel}: {metricSummary} " +
                $"Estimated stability: ~{state.StabilityProbabilityPercent:F0}%.",
        };
    }

    private static string BuildWithoutActionNarrative(
        SimulationScenarioType scenario,
        SimulatedResourceState state,
        string                 horizonLabel,
        HistoricalSummary?     summary)
    {
        string cpuNote  = state.CpuDelta  > 3 ? $"CPU may rise to ~{state.Cpu:F0}% (+{state.CpuDelta:F0}%)." : "";
        string ramNote  = state.RamDelta  > 3 ? $"RAM may rise to ~{state.Ram:F0}% (+{state.RamDelta:F0}%)." : "";
        string diskNote = state.DiskDelta > 1 ? $"Disk may reach ~{state.Disk:F0}% (+{state.DiskDelta:F0}%)." : "";

        var worsening = new[] { cpuNote, ramNote, diskNote }.Where(s => s.Length > 0).ToList();

        if (worsening.Count == 0)
            return $"Over {horizonLabel}, metrics are projected to remain relatively stable without intervention.";

        return $"Without action over {horizonLabel}: {string.Join(" ", worsening)} " +
               "Conditions may worsen gradually based on observed trends.";
    }

    private static string BuildScenarioNarrative(
        SimulationScenarioType scenario,
        SimulatedResourceState withAction,
        SimulatedResourceState withoutAction,
        SimulationConfidenceScore confidence,
        string   horizonLabel,
        bool     hasSufficientData)
    {
        string confidenceNote = hasSufficientData
            ? $"This projection is based on {confidence.OverallPercent}% confidence ({confidence.LearningDataPercent}% learning data quality)."
            : "This is an early estimate — confidence will improve with more operational history.";

        double cpuGain  = withoutAction.Cpu  - withAction.Cpu;
        double ramGain  = withoutAction.Ram  - withAction.Ram;
        double diskGain = withoutAction.Disk - withAction.Disk;

        var gains = new List<string>();
        if (cpuGain  > 2) gains.Add($"~{cpuGain:F0}% less CPU pressure");
        if (ramGain  > 2) gains.Add($"~{ramGain:F0}% less RAM usage");
        if (diskGain > 1) gains.Add($"~{diskGain:F0}% less disk pressure");

        string gainSummary = gains.Count > 0
            ? $"Taking action is projected to yield {string.Join(", ", gains)} compared to doing nothing. "
            : "The projected difference between acting and not acting is minimal at this horizon. ";

        return gainSummary + confidenceNote +
               " All estimates are approximations — real outcomes depend on workload and system conditions.";
    }

    // ── Key insights builders ─────────────────────────────────────────────────

    private static IReadOnlyList<string> BuildKeyInsights(
        SimulationScenarioType scenario,
        SimulatedResourceState state,
        OperationalRiskProjection risk)
    {
        var insights = new List<string>();

        if (state.StartupSettlingSeconds > 30)
            insights.Add($"Post-boot settling time may decrease by ~{state.StartupSettlingSeconds / 60:F0} min.");
        if (state.EstimatedReliefDays > 0)
            insights.Add($"Storage relief estimated for ~{state.EstimatedReliefDays:F0} days.");
        if (state.StabilityProbabilityPercent >= 75)
            insights.Add($"Stability probability: ~{state.StabilityProbabilityPercent:F0}%.");
        if (!risk.IsReversible)
            insights.Add("This action is not reversible — review carefully before proceeding.");
        if (risk.RecurrenceLikelihoodPercent >= 60)
            insights.Add($"Issue recurrence is likely (~{risk.RecurrenceLikelihoodPercent}%) — address root cause for lasting relief.");

        if (insights.Count == 0)
            insights.Add("No significant projected impacts beyond metric changes.");

        return insights;
    }

    private static IReadOnlyList<string> BuildNoActionInsights(
        SimulatedResourceState state,
        HistoricalSummary?     summary)
    {
        var insights = new List<string>();

        if (state.CpuDelta > 5)
            insights.Add($"CPU trending upward — may reach {state.Cpu:F0}% without intervention.");
        if (state.RamDelta > 5)
            insights.Add($"RAM trending upward — may reach {state.Ram:F0}%.");
        if (state.Disk > 90)
            insights.Add($"Disk is projected to reach {state.Disk:F0}% — performance may degrade significantly.");

        if (summary?.RecurringPatterns.Any(p => p.IsEscalating) ?? false)
            insights.Add("One or more recurring issues are escalating in frequency.");

        if (insights.Count == 0)
            insights.Add("Conditions are projected to remain relatively stable without intervention.");

        return insights;
    }

    // ── Historical basis ──────────────────────────────────────────────────────

    private static IReadOnlyList<HistoricalBasisEntry> BuildHistoricalBasis(
        HistoricalSummary?                                              summary,
        IReadOnlyDictionary<string, RecommendationEffectivenessProfile> profiles,
        SimulationScenarioType                                          scenario)
    {
        var basis = new List<HistoricalBasisEntry>();

        if (summary is not null)
        {
            long rows = summary.TotalTelemetryRows;
            basis.Add(new HistoricalBasisEntry(
                DataSource:          "Telemetry history",
                Description:         $"{rows:N0} sampled telemetry readings provide trend data for CPU, RAM, and disk.",
                ContributionPercent: rows >= 5000 ? 40 : rows >= 500 ? 25 : 10));

            if (summary.TotalInsightEvents > 0)
                basis.Add(new HistoricalBasisEntry(
                    DataSource:          "Insight history",
                    Description:         $"{summary.TotalInsightEvents:N0} insight onset/resolution events inform pattern frequency.",
                    ContributionPercent: 25));

            if (summary.RecurringPatterns.Count > 0)
                basis.Add(new HistoricalBasisEntry(
                    DataSource:          "Recurring patterns",
                    Description:         $"{summary.RecurringPatterns.Count} recurring system patterns detected across the monitoring window.",
                    ContributionPercent: 20));
        }

        var warm = profiles.Values.Where(p => p.IsWarmEnough).ToList();
        if (warm.Count > 0)
        {
            int totalExecs = warm.Sum(p => p.TotalAnalyzed);
            basis.Add(new HistoricalBasisEntry(
                DataSource:          "Remediation learning",
                Description:         $"{totalExecs} action executions with measured before/after outcomes inform effectiveness estimates.",
                ContributionPercent: 30));
        }
        else
        {
            basis.Add(new HistoricalBasisEntry(
                DataSource:          "Default estimates",
                Description:         "No prior action outcomes recorded — conservative default estimates are used.",
                ContributionPercent: 10));
        }

        return basis;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static double ComputeNoActionStability(
        HistoricalSummary? summary,
        double cpu, double ram, double disk)
    {
        // Base stability from current metric levels
        double base_ = 75.0;
        if (cpu  > 80) base_ -= 15;
        if (ram  > 85) base_ -= 15;
        if (disk > 90) base_ -= 20;

        // Historical health score influence
        if (summary is not null)
        {
            double healthPenalty = Math.Max(0, (90 - summary.SevenDayHealth.Score) * 0.4);
            base_ -= healthPenalty;
        }

        return Math.Clamp(base_, 20, 85);
    }

    private static string ScenarioVerb(SimulationScenarioType scenario) => scenario switch
    {
        SimulationScenarioType.DisableStartupApps    => "startup app disable",
        SimulationScenarioType.CleanupDisk           => "disk cleanup",
        SimulationScenarioType.RepairWindows         => "Windows repair",
        SimulationScenarioType.TerminateProcess      => "process termination",
        SimulationScenarioType.RestartSystem         => "system restart",
        SimulationScenarioType.DeferMaintenance      => "maintenance deferral",
        SimulationScenarioType.ContinueCurrentWorkload => "workload continuation",
        SimulationScenarioType.StorageGrowthContinuation => "storage growth continuation",
        SimulationScenarioType.WorkloadStress        => "workload stress test",
        _                                            => "this action",
    };

    private static string HorizonLabel(SimulationHorizon horizon) => horizon switch
    {
        SimulationHorizon.FifteenMinutes  => "15 minutes",
        SimulationHorizon.OneHour         => "1 hour",
        SimulationHorizon.FourHours       => "4 hours",
        SimulationHorizon.TwentyFourHours => "24 hours",
        SimulationHorizon.SevenDays       => "7 days",
        _                                 => "the selected horizon",
    };

    private static LongTermMetricTrend NoTrend(string name) =>
        new(MetricName:   name,
            SevenDayAvg:  null,
            ThirtyDayAvg: null,
            Direction:    TrendDirection.Stable,
            ChangePercent: 0,
            TrendLabel:   "No data");

    private static SimulationScenario BuildCancelledScenario(SimulationInput input) =>
        new()
        {
            ScenarioType     = input.ScenarioType,
            Input            = input,
            NarrativeSummary = "Simulation was cancelled.",
            SimulatedAt      = DateTimeOffset.Now,
            HasSufficientData = false,
            Confidence       = new(0, 0, 0, 0, "Cancelled.", []),
            Risk             = new(ProjectedRiskLevel.Low, "N/A", true, 0, 0, [], []),
        };

    private static SimulationScenario BuildErrorScenario(SimulationInput input, string error) =>
        new()
        {
            ScenarioType     = input.ScenarioType,
            Input            = input,
            NarrativeSummary = $"Simulation encountered an error: {error}",
            SimulatedAt      = DateTimeOffset.Now,
            HasSufficientData = false,
            Confidence       = new(0, 0, 0, 0, "Error occurred.", []),
            Risk             = new(ProjectedRiskLevel.Low, "N/A", true, 0, 0, [], []),
        };
}
