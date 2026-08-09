using Lucid.Services.Baseline;

namespace Lucid.Services.Intelligence;

// ── Interface ───────────────────────────────────────────────────────────────────

/// <summary>
/// Provides a clean view of this machine's learned behavioral norms,
/// adapted from the live <see cref="ISystemBaselineService.CurrentBaseline"/> Welford model.
/// Stateless — no Start()/Stop() required.
/// </summary>
public interface IBehavioralBaselineService
{
    /// <summary>
    /// Returns the current behavioral baseline snapshot, or null when the Welford
    /// model has not yet accumulated enough observations (warm-up guard active).
    /// </summary>
    BehavioralBaseline? GetBaseline();

    /// <summary>True when a reliable baseline exists (≥ 200 observations for CPU and RAM).</summary>
    bool HasValidBaseline { get; }
}

// ── Implementation ──────────────────────────────────────────────────────────────

/// <summary>
/// Thin adapter over <see cref="ISystemBaselineService"/> that exposes a clean
/// <see cref="BehavioralBaseline"/> record to the Intelligence layer and ViewModels.
///
/// Design notes:
/// • No background thread — reads the live Welford accumulators on-demand.
/// • All computed properties in <see cref="BehavioralBaseline"/> are display-ready.
/// • Registered in the composition root after the baseline service has started.
/// </summary>
public sealed class BehavioralBaselineService : IBehavioralBaselineService
{
    private readonly ISystemBaselineService _baseline;

    public BehavioralBaselineService(ISystemBaselineService baseline)
    {
        _baseline = baseline;
    }

    /// <inheritdoc/>
    public bool HasValidBaseline => _baseline.CurrentBaseline is not null;

    /// <inheritdoc/>
    public BehavioralBaseline? GetBaseline()
    {
        var raw = _baseline.CurrentBaseline;
        if (raw is null) return null;

        return new BehavioralBaseline(
            IdleCpuMean:        raw.IdleCpuMean,
            IdleCpuStdDev:      raw.IdleCpuStdDev,
            IdleCpuSamples:     raw.IdleCpuSamples,

            NormalRamMean:      raw.NormalRamMean,
            NormalRamStdDev:    raw.NormalRamStdDev,
            NormalRamSamples:   raw.NormalRamSamples,

            NormalThermalMean:  raw.NormalThermalMean,
            NormalThermalStdDev: raw.NormalThermalStdDev,
            ThermalSamples:     raw.NormalThermalSamples,

            IsReliable:  raw.IdleCpuReady && raw.NormalRamReady,
            CapturedAt:  DateTime.UtcNow);
    }
}
