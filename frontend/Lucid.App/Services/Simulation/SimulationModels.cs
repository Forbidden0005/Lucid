namespace Lucid.Services.Simulation;

// ── Scenario type ──────────────────────────────────────────────────────────────

/// <summary>
/// The class of intervention or condition being simulated.
/// Each type maps to a distinct impact estimator path.
/// </summary>
public enum SimulationScenarioType
{
    /// <summary>Simulate disabling one or more startup applications.</summary>
    DisableStartupApps,

    /// <summary>Simulate running disk cleanup (temp files, recycle bin, browser cache).</summary>
    CleanupDisk,

    /// <summary>Simulate running Windows repair tools (SFC / DISM).</summary>
    RepairWindows,

    /// <summary>Simulate terminating a resource-heavy process.</summary>
    TerminateProcess,

    /// <summary>Simulate a system restart/reboot.</summary>
    RestartSystem,

    /// <summary>Simulate what happens if maintenance is deferred for several days.</summary>
    DeferMaintenance,

    /// <summary>Simulate continuation of the current workload without intervention.</summary>
    ContinueCurrentWorkload,

    /// <summary>Simulate disk growth continuing at the current rate.</summary>
    StorageGrowthContinuation,

    /// <summary>Simulate the impact of an elevated-intensity workload.</summary>
    WorkloadStress,
}

// ── Branch type ────────────────────────────────────────────────────────────────

/// <summary>Which projected trajectory this branch represents.</summary>
public enum SimulationBranchType
{
    /// <summary>The intervention is applied at t=0.</summary>
    WithAction,

    /// <summary>No intervention — current trend continues.</summary>
    WithoutAction,

    /// <summary>Best-case outcome within plausible bounds.</summary>
    Optimistic,

    /// <summary>Worst-case outcome within plausible bounds.</summary>
    Conservative,
}

// ── Simulation horizon ─────────────────────────────────────────────────────────

/// <summary>
/// How far into the future the simulation projects.
/// Value is the horizon in minutes.
/// </summary>
public enum SimulationHorizon
{
    FifteenMinutes  = 15,
    OneHour         = 60,
    FourHours       = 240,
    TwentyFourHours = 1440,
    SevenDays       = 10080,
}

// ── Risk level ─────────────────────────────────────────────────────────────────

public enum ProjectedRiskLevel { Low, Moderate, Elevated, High }

// ── Trajectory data point ──────────────────────────────────────────────────────

/// <summary>A single projected metric snapshot at a specific time offset.</summary>
public sealed record ProjectedMetricPoint(
    TimeSpan Offset,
    double   Cpu,
    double   Ram,
    double   Disk,
    double   ThermalC);

// ── Simulated resource state ───────────────────────────────────────────────────

/// <summary>
/// Projected system state at a point in the future, after the simulation horizon.
/// All values are estimates with bounded uncertainty.
/// </summary>
public sealed record SimulatedResourceState(
    /// <summary>Projected CPU utilisation %.</summary>
    double Cpu,

    /// <summary>Projected RAM utilisation %.</summary>
    double Ram,

    /// <summary>Projected disk utilisation %.</summary>
    double Disk,

    /// <summary>Projected CPU temperature °C (0 when not available).</summary>
    double ThermalC,

    /// <summary>Change from current CPU (negative = improvement).</summary>
    double CpuDelta,

    /// <summary>Change from current RAM.</summary>
    double RamDelta,

    /// <summary>Change from current disk.</summary>
    double DiskDelta,

    /// <summary>Change from current temperature.</summary>
    double ThermalDelta,

    /// <summary>Estimated post-boot startup settling time in seconds.</summary>
    double StartupSettlingSeconds,

    /// <summary>Estimated probability that system remains stable (0–100).</summary>
    double StabilityProbabilityPercent,

    /// <summary>
    /// For cleanup scenarios: estimated days before disk pressure returns.
    /// Zero for non-storage scenarios.
    /// </summary>
    double EstimatedReliefDays,

    /// <summary>One-sentence summary of the primary projected impact.</summary>
    string PrimaryImpactSummary);

// ── Simulation branch ──────────────────────────────────────────────────────────

/// <summary>
/// A single projected trajectory through the simulation horizon.
/// A scenario always has WithAction + WithoutAction branches.
/// Optimistic and Conservative branches are optional.
/// </summary>
public sealed record SimulationBranch(
    SimulationBranchType                BranchType,
    string                              Label,
    string                              Description,
    SimulatedResourceState              ProjectedState,
    IReadOnlyList<ProjectedMetricPoint> Trajectory,
    int                                 ConfidencePercent,
    string                              NarrativeSummary,
    IReadOnlyList<string>               KeyInsights);

// ── Confidence model ───────────────────────────────────────────────────────────

/// <summary>
/// Breakdown of what drives the simulation's confidence score.
/// Confidence is always shown to the user so they understand the estimate's basis.
/// </summary>
public sealed record SimulationConfidenceScore(
    /// <summary>Overall confidence in [0, 100].</summary>
    int                   OverallPercent,

    /// <summary>Contribution from telemetry history coverage (more rows = higher).</summary>
    int                   DataCoveragePercent,

    /// <summary>Contribution from insight history depth.</summary>
    int                   HistoricalBasisPercent,

    /// <summary>Contribution from remediation learning effectiveness data.</summary>
    int                   LearningDataPercent,

    /// <summary>Plain-English explanation of the confidence level.</summary>
    string                ExplanationText,

    /// <summary>Bullet-point factors that raise or lower confidence.</summary>
    IReadOnlyList<string> Factors);

// ── Operational risk projection ────────────────────────────────────────────────

/// <summary>
/// Projected risk profile for the simulated intervention.
/// Covers reversibility, recurrence, and rollback likelihood.
/// </summary>
public sealed record OperationalRiskProjection(
    ProjectedRiskLevel    Level,
    string                Label,
    bool                  IsReversible,
    int                   RecurrenceLikelihoodPercent,
    int                   RollbackLikelihoodPercent,
    IReadOnlyList<string> RiskFactors,
    IReadOnlyList<string> Mitigations);

// ── Historical basis entry ─────────────────────────────────────────────────────

/// <summary>
/// Describes one data source that informed the simulation estimate.
/// Surfaces what historical evidence underpins each projection.
/// </summary>
public sealed record HistoricalBasisEntry(
    string DataSource,
    string Description,
    int    ContributionPercent);

// ── Simulation input ───────────────────────────────────────────────────────────

/// <summary>
/// User-provided parameters describing what should be simulated.
/// All fields default to sensible values so the engine is cold-start safe.
/// </summary>
public sealed record SimulationInput(
    SimulationScenarioType              ScenarioType,
    SimulationHorizon                   Horizon,
    IReadOnlyDictionary<string, string>? Parameters = null);

// ── Simulation scenario ────────────────────────────────────────────────────────

/// <summary>
/// Complete output of <see cref="OperationalSimulationEngine.SimulateAsync"/>.
/// Contains all branches, confidence scoring, risk projection, and historical basis.
/// </summary>
public sealed class SimulationScenario
{
    public SimulationScenarioType             ScenarioType     { get; init; }
    public SimulationInput                    Input            { get; init; } = null!;
    public IReadOnlyList<SimulationBranch>    Branches         { get; init; } = [];
    public SimulationConfidenceScore          Confidence       { get; init; } = null!;
    public OperationalRiskProjection          Risk             { get; init; } = null!;
    public IReadOnlyList<HistoricalBasisEntry> HistoricalBasis { get; init; } = [];
    public string                             NarrativeSummary { get; init; } = "";
    public DateTimeOffset                     SimulatedAt      { get; init; }
    public bool                               HasSufficientData { get; init; }

    public SimulationBranch? WithActionBranch =>
        Branches.FirstOrDefault(b => b.BranchType == SimulationBranchType.WithAction);

    public SimulationBranch? WithoutActionBranch =>
        Branches.FirstOrDefault(b => b.BranchType == SimulationBranchType.WithoutAction);
}
