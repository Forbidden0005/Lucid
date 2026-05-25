using Lucid.Services.Analytics;

namespace Lucid.Services.Simulation;

/// <summary>
/// Projects metric trajectories forward in time using linear extrapolation
/// of observed historical trends.
///
/// Model philosophy:
///   • Linear extrapolation is intentionally simple and honest. Real-world
///     metrics are not perfectly linear, so projections carry inherent uncertainty
///     that grows with forecast horizon length.
///   • Branch type modifies both the starting point and the trend slope:
///       WithAction     — applies the intervention delta at t=0; slope partially reset
///       WithoutAction  — current trend continues unchanged
///       Optimistic     — trend slopes clamped to zero or improving
///       Conservative   — trend slopes amplified by 1.5×
///   • All projected values are clamped to [0, 100] (or [0, 110] for thermal).
///
/// All methods are deterministic and pure. No state, no I/O.
/// </summary>
public static class ResourceTrajectoryProjector
{
    private const int MaxSamples    = 16;
    private const int MinSamples    = 5;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Projects a resource trajectory for the given horizon and branch type.
    ///
    /// Returns a list of <see cref="ProjectedMetricPoint"/> from t=0 to t=horizon,
    /// sampled at regular intervals (at most <c>MaxSamples</c> points).
    /// </summary>
    public static IReadOnlyList<ProjectedMetricPoint> Project(
        double              currentCpu,
        double              currentRam,
        double              currentDisk,
        double              currentThermal,
        LongTermMetricTrend cpuTrend,
        LongTermMetricTrend ramTrend,
        LongTermMetricTrend diskTrend,
        SimulationHorizon   horizon,
        SimulationBranchType branchType,
        SimulatedResourceState? appliedImpact = null)
    {
        int totalMinutes = (int)horizon;
        int sampleCount  = ChooseSampleCount(totalMinutes);
        int stepMinutes  = totalMinutes / sampleCount;

        // Convert historical trends to per-minute slope values
        double cpuSlope  = TrendToSlopePerMinute(cpuTrend);
        double ramSlope  = TrendToSlopePerMinute(ramTrend);
        double diskSlope = TrendToSlopePerMinute(diskTrend);

        // Starting point — apply intervention impact for WithAction branch
        double startCpu   = currentCpu;
        double startRam   = currentRam;
        double startDisk  = currentDisk;
        double startTherm = currentThermal;

        switch (branchType)
        {
            case SimulationBranchType.WithAction when appliedImpact is not null:
                startCpu   = Math.Clamp(currentCpu   + appliedImpact.CpuDelta,   0, 100);
                startRam   = Math.Clamp(currentRam   + appliedImpact.RamDelta,   0, 100);
                startDisk  = Math.Clamp(currentDisk  + appliedImpact.DiskDelta,  0, 100);
                startTherm = Math.Clamp(currentThermal + appliedImpact.ThermalDelta, 0, 110);
                // After remediation, the underlying trend partially resets
                cpuSlope  *= 0.35;
                ramSlope  *= 0.35;
                diskSlope *= 0.25;
                break;

            case SimulationBranchType.Optimistic:
                // Best case: worsening trends stop
                cpuSlope  = Math.Min(cpuSlope, 0);
                ramSlope  = Math.Min(ramSlope, 0);
                diskSlope = Math.Min(diskSlope, 0);
                break;

            case SimulationBranchType.Conservative:
                // Worst case: worsening trends accelerate
                if (cpuSlope  > 0) cpuSlope  *= 1.6;
                if (ramSlope  > 0) ramSlope  *= 1.6;
                if (diskSlope > 0) diskSlope *= 1.6;
                break;
        }

        var points = new List<ProjectedMetricPoint>(sampleCount + 1)
        {
            // t = 0 is always the starting state
            new(TimeSpan.Zero, startCpu, startRam, startDisk, startTherm)
        };

        for (var i = 1; i <= sampleCount; i++)
        {
            int minutes = i * stepMinutes;

            // Linear projection with per-branch slope
            double cpu   = Math.Clamp(startCpu   + cpuSlope  * minutes, 0, 100);
            double ram   = Math.Clamp(startRam   + ramSlope  * minutes, 0, 100);
            double disk  = Math.Clamp(startDisk  + diskSlope * minutes, 0, 100);

            // Thermal: only project upward trends, otherwise assume stable
            double thermalSlope = branchType == SimulationBranchType.Conservative ? 0.003 : 0;
            double therm = Math.Clamp(startTherm + thermalSlope * minutes, 0, 110);

            points.Add(new(TimeSpan.FromMinutes(minutes), cpu, ram, disk, therm));
        }

        return points;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int ChooseSampleCount(int totalMinutes) =>
        totalMinutes switch
        {
            <= 60   => MinSamples,
            <= 240  => 8,
            <= 1440 => 12,
            _       => MaxSamples,
        };

    /// <summary>
    /// Converts a <see cref="LongTermMetricTrend"/> into an approximate
    /// per-minute drift rate.
    ///
    /// The ChangePercent field is the absolute difference between the 7-day
    /// average and the 30-day average. We treat this as drift accumulated over
    /// ~23 days (the midpoint difference between the two windows), giving:
    ///   slope = ChangePercent / (23 × 24 × 60) percentage-points per minute
    ///
    /// A noise floor of ±2 % is applied — changes smaller than this are
    /// treated as stable.
    /// </summary>
    private static double TrendToSlopePerMinute(LongTermMetricTrend trend)
    {
        if (trend.SevenDayAvg is null || trend.ThirtyDayAvg is null)
            return 0;

        double change = trend.ChangePercent; // percentage-point delta
        if (Math.Abs(change) < 2.0) return 0; // noise floor

        // Negative slope = improving (lower is better for CPU/RAM/Disk)
        const double windowMinutes = 23.0 * 24 * 60; // ~23-day diff between window centres
        return change / windowMinutes;
    }
}
