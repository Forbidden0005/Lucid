namespace Lucid.Services.Baseline;

/// <summary>
/// Immutable snapshot of the learned behavioural norms for this specific machine.
///
/// Built by <see cref="SystemBaselineService"/> from its live <see cref="WelfordStats"/>
/// accumulators. Insight rules receive this record via <see cref="ISystemBaselineService.CurrentBaseline"/>
/// and use it to distinguish "objectively high" from "unusually high for THIS machine."
///
/// Anomaly detection:
///   Four metrics are modelled independently:
///     • Idle CPU   — sampled only when system load is below 40 %;
///                    represents the machine's natural floor (background services,
///                    kernel overhead, antivirus idle scans, etc.)
///     • Normal RAM — sampled unconditionally; reflects the machine's typical
///                    memory footprint for the user's usual workload.
///     • Thermal    — sampled when the ACPI temperature sensor is available;
///                    reflects the machine's thermal envelope under normal use.
///     • Disk I/O   — combined read + write MB/s; reflects typical I/O activity.
///
///   Sigma thresholds:
///     CPU / Thermal — 2.0σ above mean (≈ 98th percentile of a normal distribution)
///     RAM           — 2.5σ above mean (RAM varies less, so a higher bar is appropriate)
///
///   Absolute delta floors prevent flagging trivial noise:
///     CPU    ≥ 15 % above idle mean
///     RAM    ≥ 10 % above normal mean
///     Thermal ≥  5 °C above normal mean
///
/// Warm-up guard:
///   All anomaly methods return false / 0 when <see cref="WelfordStats.MinAnomalyCount"/>
///   has not been reached. Rules never fire prematurely on a fresh install.
/// </summary>
public sealed record MachineBaseline
{
    // ── Anomaly thresholds ────────────────────────────────────────────────────

    /// <summary>
    /// Z-score at which idle CPU is considered anomalously elevated.
    /// 2.0σ corresponds to the ≈ 98th percentile of a symmetric distribution.
    /// </summary>
    public const double CpuSigmaRecommendation  = 2.0;
    public const double CpuSigmaWarning         = 3.0;

    /// <summary>Slightly higher threshold for RAM (it varies less).</summary>
    public const double RamSigmaRecommendation  = 2.5;
    public const double RamSigmaWarning         = 3.5;

    public const double ThermalSigmaRecommendation = 2.0;
    public const double ThermalSigmaWarning        = 3.0;

    // Absolute delta floors — prevents flagging a jump from 4 % to 16 %
    // when the machine normally idles at 4 % ± 1 %.
    public const double CpuMinAbsoluteDelta     = 15.0;
    public const double RamMinAbsoluteDelta     = 10.0;
    public const double ThermalMinAbsoluteDelta =  5.0;

    // ── Idle CPU model ────────────────────────────────────────────────────────

    /// <summary>Mean CPU % during idle periods (system load below 40 %).</summary>
    public double IdleCpuMean    { get; init; }

    /// <summary>Standard deviation of idle CPU observations.</summary>
    public double IdleCpuStdDev  { get; init; }

    /// <summary>Number of idle-period samples incorporated into this model.</summary>
    public long   IdleCpuSamples { get; init; }

    /// <summary>True when the idle CPU accumulator has enough data for anomaly detection.</summary>
    public bool   IdleCpuReady   => IdleCpuSamples >= WelfordStats.MinAnomalyCount;

    // ── Normal RAM model ──────────────────────────────────────────────────────

    public double NormalRamMean    { get; init; }
    public double NormalRamStdDev  { get; init; }
    public long   NormalRamSamples { get; init; }
    public bool   NormalRamReady   => NormalRamSamples >= WelfordStats.MinAnomalyCount;

    // ── Thermal model ─────────────────────────────────────────────────────────

    /// <summary>Mean CPU temperature °C across all periods with a valid sensor reading.</summary>
    public double NormalThermalMean    { get; init; }
    public double NormalThermalStdDev  { get; init; }
    public long   NormalThermalSamples { get; init; }
    public bool   NormalThermalReady   => NormalThermalSamples >= WelfordStats.MinAnomalyCount;

    // ── Disk I/O model ────────────────────────────────────────────────────────

    public double NormalDiskIoMean    { get; init; }
    public double NormalDiskIoStdDev  { get; init; }
    public long   NormalDiskIoSamples { get; init; }

    // ── Startup patterns ──────────────────────────────────────────────────────

    /// <summary>Number of startup entries last seen in the StartupSampler output.</summary>
    public int StartupEntryCount      { get; init; }

    /// <summary>Number of high-impact startup apps last detected.</summary>
    public int HighImpactStartupCount { get; init; }

    // ── Provenance ────────────────────────────────────────────────────────────

    /// <summary>Total telemetry ticks observed (includes all metrics).</summary>
    public long           TotalObservations { get; init; }

    /// <summary>Timestamp of the first observation recorded into this baseline.</summary>
    public DateTimeOffset LearnedSince      { get; init; }

    // ── Z-score helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the idle-CPU Z-score for the given CPU usage percentage.
    /// A Z-score of 2.0 means "2 standard deviations above this machine's
    /// normal idle level." Returns 0 when data is insufficient.
    /// </summary>
    public double IdleCpuZScore(double cpuPct) =>
        IdleCpuReady && IdleCpuStdDev >= 0.5
            ? (cpuPct - IdleCpuMean) / IdleCpuStdDev
            : 0;

    public double RamZScore(double ramPct) =>
        NormalRamReady && NormalRamStdDev >= 0.5
            ? (ramPct - NormalRamMean) / NormalRamStdDev
            : 0;

    public double ThermalZScore(double tempC) =>
        NormalThermalReady && NormalThermalStdDev >= 0.5
            ? (tempC - NormalThermalMean) / NormalThermalStdDev
            : 0;

    // ── Anomaly predicates ────────────────────────────────────────────────────

    /// <summary>
    /// True when <paramref name="cpuPct"/> is anomalously high relative to
    /// this machine's learned idle baseline.
    ///
    /// Requires both a statistical threshold (Z ≥ 2.0) and an absolute
    /// delta floor (≥ 15 %) to avoid flagging trivial noise.
    /// </summary>
    public bool IsIdleCpuAnomaly(double cpuPct) =>
        IdleCpuReady
        && cpuPct - IdleCpuMean >= CpuMinAbsoluteDelta
        && IdleCpuZScore(cpuPct) >= CpuSigmaRecommendation;

    /// <summary>
    /// True when RAM usage is anomalously high for this machine.
    /// </summary>
    public bool IsRamAnomaly(double ramPct) =>
        NormalRamReady
        && ramPct - NormalRamMean >= RamMinAbsoluteDelta
        && RamZScore(ramPct) >= RamSigmaRecommendation;

    /// <summary>
    /// True when the CPU temperature is anomalously high for this machine.
    /// </summary>
    public bool IsThermalAnomaly(double tempC) =>
        NormalThermalReady
        && tempC - NormalThermalMean >= ThermalMinAbsoluteDelta
        && ThermalZScore(tempC) >= ThermalSigmaRecommendation;

    // ── Confidence helper ─────────────────────────────────────────────────────

    /// <summary>
    /// Maps a Z-score to a confidence percentage for display in insight cards.
    ///
    /// Mapping:
    ///   Z = 2.0 → 70 %   (threshold-level evidence)
    ///   Z = 3.0 → 82 %   (moderate evidence)
    ///   Z = 4.0 → 94 %   (strong evidence, capped at 95 %)
    /// </summary>
    public static int ConfidenceFromZScore(double zscore) =>
        Math.Clamp((int)(46 + zscore * 12), 70, 95);
}
