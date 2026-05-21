namespace ExplainMyPC.Services.Replay;

/// <summary>
/// Computes what changed between two system state snapshots.
///
/// The delta covers:
///   • Per-metric telemetry shifts (CPU, RAM, GPU, Disk, Temperature)
///   • Insight changes (new onsets, resolutions)
///   • Actions executed in the interval
///   • Human-readable summary lines describing the most notable changes
///
/// All percentage deltas are computed as absolute-point differences
/// (e.g., CPU went from 45 % to 72 % = +27 pp).
/// </summary>
internal static class OperationalDeltaAnalyzer
{
    // Thresholds for "significant" change — below these the delta is noise
    private const double CpuSignificantThreshold  = 5.0;
    private const double RamSignificantThreshold  = 4.0;
    private const double GpuSignificantThreshold  = 8.0;
    private const double DiskSignificantThreshold = 5.0;
    private const double TempSignificantThreshold = 3.0;

    /// <summary>
    /// Computes the full operational delta between two system state snapshots.
    /// </summary>
    public static OperationalDelta Compute(SystemStateSnapshot from, SystemStateSnapshot to)
    {
        // ── Telemetry deltas ──────────────────────────────────────────────────
        double cpuChange  = to.Telemetry.CpuPercent           - from.Telemetry.CpuPercent;
        double ramChange  = to.Telemetry.RamPercent           - from.Telemetry.RamPercent;
        double gpuChange  = to.Telemetry.GpuPercent           - from.Telemetry.GpuPercent;
        double diskChange = to.Telemetry.DiskPercent          - from.Telemetry.DiskPercent;
        double tempChange = to.Telemetry.TemperatureCelsius   - from.Telemetry.TemperatureCelsius;

        // ── Insight diffs ─────────────────────────────────────────────────────
        var fromIds = from.ActiveInsights.Select(i => i.InsightId).ToHashSet();
        var toIds   = to.ActiveInsights.Select(i => i.InsightId).ToHashSet();

        var newInsights      = to.ActiveInsights
            .Where(i => !fromIds.Contains(i.InsightId))
            .Select(i => i.Title)
            .ToList();

        var resolvedInsights = from.ActiveInsights
            .Where(i => !toIds.Contains(i.InsightId))
            .Select(i => i.Title)
            .ToList();

        // ── Action diffs ──────────────────────────────────────────────────────
        var fromActionIds = from.ActionsToNow.Select(a => a.Id).ToHashSet();
        var newActions    = to.ActionsToNow
            .Where(a => !fromActionIds.Contains(a.Id) && a.IsSuccess && !a.IsDryRun)
            .Select(a => a.ActionTitle)
            .ToList();

        // ── Summary lines ─────────────────────────────────────────────────────
        var summaryLines = BuildSummaryLines(
            cpuChange, ramChange, gpuChange, diskChange, tempChange,
            newInsights, resolvedInsights, newActions, from, to);

        // ── Significance and direction ────────────────────────────────────────
        bool hasSignificantChange =
            Math.Abs(cpuChange)  >= CpuSignificantThreshold  ||
            Math.Abs(ramChange)  >= RamSignificantThreshold  ||
            Math.Abs(gpuChange)  >= GpuSignificantThreshold  ||
            Math.Abs(diskChange) >= DiskSignificantThreshold ||
            Math.Abs(tempChange) >= TempSignificantThreshold ||
            newInsights.Count > 0 || resolvedInsights.Count > 0;

        var direction = ComputeDirection(
            cpuChange, ramChange, tempChange,
            newInsights.Count, resolvedInsights.Count, newActions.Count);

        return new OperationalDelta(
            FromTime:             from.Timestamp,
            ToTime:               to.Timestamp,
            CpuChange:            cpuChange,
            RamChange:            ramChange,
            GpuChange:            gpuChange,
            DiskChange:           diskChange,
            TempChange:           tempChange,
            NewInsights:          newInsights,
            ResolvedInsights:     resolvedInsights,
            NewActions:           newActions,
            SummaryLines:         summaryLines,
            HasSignificantChange: hasSignificantChange,
            Direction:            direction);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static List<string> BuildSummaryLines(
        double             cpuChange,
        double             ramChange,
        double             gpuChange,
        double             diskChange,
        double             tempChange,
        List<string>       newInsights,
        List<string>       resolvedInsights,
        List<string>       newActions,
        SystemStateSnapshot from,
        SystemStateSnapshot to)
    {
        var lines = new List<string>();

        // Metric changes
        if (Math.Abs(cpuChange) >= CpuSignificantThreshold)
            lines.Add(cpuChange > 0
                ? $"CPU usage increased from {from.Telemetry.CpuPercent:F0}% to {to.Telemetry.CpuPercent:F0}% (+{cpuChange:F0} pp)"
                : $"CPU usage decreased from {from.Telemetry.CpuPercent:F0}% to {to.Telemetry.CpuPercent:F0}% ({cpuChange:F0} pp)");

        if (Math.Abs(ramChange) >= RamSignificantThreshold)
            lines.Add(ramChange > 0
                ? $"RAM usage rose from {from.Telemetry.RamPercent:F0}% to {to.Telemetry.RamPercent:F0}% (+{ramChange:F0} pp)"
                : $"RAM usage dropped from {from.Telemetry.RamPercent:F0}% to {to.Telemetry.RamPercent:F0}% ({ramChange:F0} pp)");

        if (from.Telemetry.HasTemperature && to.Telemetry.HasTemperature &&
            Math.Abs(tempChange) >= TempSignificantThreshold)
            lines.Add(tempChange > 0
                ? $"CPU temperature rose by {tempChange:F0}°C"
                : $"CPU temperature dropped by {Math.Abs(tempChange):F0}°C");

        if (Math.Abs(diskChange) >= DiskSignificantThreshold)
            lines.Add(diskChange > 0
                ? $"Disk activity increased by {diskChange:F0} pp"
                : $"Disk activity decreased by {Math.Abs(diskChange):F0} pp");

        // Insight changes
        foreach (var insight in newInsights)
            lines.Add($"New finding: {insight}");
        foreach (var insight in resolvedInsights)
            lines.Add($"Resolved: {insight}");

        // Actions taken
        foreach (var action in newActions)
            lines.Add($"Action completed: {action}");

        if (lines.Count == 0)
            lines.Add("No significant changes detected.");

        return lines;
    }

    private static DeltaDirection ComputeDirection(
        double cpuChange,
        double ramChange,
        double tempChange,
        int    newInsightCount,
        int    resolvedInsightCount,
        int    actionCount)
    {
        // Score: positive = worsening, negative = improving
        int score = 0;

        if (cpuChange  > CpuSignificantThreshold)  score++;
        if (cpuChange  < -CpuSignificantThreshold) score--;
        if (ramChange  > RamSignificantThreshold)  score++;
        if (ramChange  < -RamSignificantThreshold) score--;
        if (tempChange > TempSignificantThreshold) score++;
        if (tempChange < -TempSignificantThreshold)score--;

        score += newInsightCount;
        score -= resolvedInsightCount;

        // Actions slightly bias toward improving (they were taken to help)
        if (actionCount > 0) score--;

        return score > 0 ? DeltaDirection.Worsening
             : score < 0 ? DeltaDirection.Improving
             :              DeltaDirection.Stable;
    }
}
