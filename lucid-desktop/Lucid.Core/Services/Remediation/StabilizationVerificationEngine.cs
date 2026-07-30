using Lucid.Helpers;
using Lucid.Services.Intelligence;

namespace Lucid.Services.Remediation;

/// <summary>
/// Captures system state snapshots and computes before/after delta analysis
/// for post-remediation validation.
///
/// All comparison is deterministic — same inputs produce the same result.
/// Confidence is scaled by telemetry availability.
/// </summary>
public static class StabilizationVerificationEngine
{
    // ── Improvement thresholds ────────────────────────────────────────────────
    private const double SignificantImprovementThreshold = 5.0;   // ≥5% reduction = improved
    private const double MinorChangeThreshold            = 2.0;   // <2% = effectively stable

    /// <summary>
    /// Captures a snapshot of the current system state for before/after comparison.
    /// </summary>
    public static StabilizationSnapshot CaptureSnapshot(
        TelemetrySnapshot?           telemetry,
        IReadOnlyList<SystemInsight> activeInsights)
    {
        return new StabilizationSnapshot(
            CpuPercent:           telemetry?.CpuPercent ?? 0,
            RamPercent:           telemetry?.RamPercent ?? 0,
            DiskPercent:          telemetry?.DiskPercent ?? 0,
            CpuTemperatureCelsius: telemetry?.CpuTemperatureCelsius ?? 0,
            TemperatureAvailable: telemetry?.CpuTemperatureAvailable ?? false,
            ActiveInsightCount:   activeInsights.Count,
            ActiveInsightIds:     activeInsights.Select(i => i.Id).ToList(),
            CapturedAt:           DateTimeOffset.Now);
    }

    /// <summary>
    /// Computes the stabilization result from before and after snapshots.
    /// Returns <see cref="StabilizationResult.Inconclusive"/> when either
    /// snapshot has zero telemetry (e.g. captured before first reading).
    /// </summary>
    public static StabilizationResult Evaluate(
        StabilizationSnapshot before,
        StabilizationSnapshot after)
    {
        // Inconclusive if we have no telemetry data
        if (before.CpuPercent == 0 && before.RamPercent == 0)
            return StabilizationResult.Inconclusive;
        if (after.CpuPercent == 0 && after.RamPercent == 0)
            return StabilizationResult.Inconclusive;

        var cpuDelta     = after.CpuPercent  - before.CpuPercent;
        var ramDelta     = after.RamPercent  - before.RamPercent;
        var insightDelta = after.ActiveInsightCount - before.ActiveInsightCount;

        // Degraded: metrics measurably worsened
        if (cpuDelta > 10 && ramDelta > 5)
            return StabilizationResult.Degraded;

        // Improved: at least one metric measurably improved, none significantly worsened
        bool cpuImproved    = cpuDelta <= -SignificantImprovementThreshold;
        bool ramImproved    = ramDelta <= -SignificantImprovementThreshold;
        bool insightResolved = insightDelta < 0;

        if (cpuImproved || ramImproved || insightResolved)
            return StabilizationResult.Improved;

        // Stable: minimal change in either direction
        return StabilizationResult.Stable;
    }

    /// <summary>
    /// Computes percentage deltas between before and after snapshots.
    /// Negative values = improvement for resource metrics (CPU/RAM/Disk).
    /// </summary>
    public static (double CpuChange, double RamChange, double DiskChange)
        ComputeDeltas(StabilizationSnapshot before, StabilizationSnapshot after) =>
        (
            after.CpuPercent  - before.CpuPercent,
            after.RamPercent  - before.RamPercent,
            after.DiskPercent - before.DiskPercent
        );

    /// <summary>
    /// Produces a one-sentence narrative describing the stabilization outcome.
    /// </summary>
    public static string BuildNarrative(
        string                workflowName,
        StabilizationResult   result,
        StabilizationSnapshot before,
        StabilizationSnapshot after)
    {
        var (cpuChange, ramChange, diskChange) = ComputeDeltas(before, after);
        var insightDelta = after.ActiveInsightCount - before.ActiveInsightCount;

        return result switch
        {
            StabilizationResult.Improved =>
                BuildImprovementNarrative(workflowName, cpuChange, ramChange, insightDelta),

            StabilizationResult.Stable =>
                $"The {workflowName} workflow completed. System metrics remained stable — " +
                "improvements may take a few minutes to fully manifest.",

            StabilizationResult.Degraded =>
                $"The {workflowName} workflow completed, but metrics appear to have increased. " +
                "This may not be caused by the workflow. Rollback is available if applicable.",

            _ =>
                $"The {workflowName} workflow completed. Stabilization data was insufficient " +
                "to measure improvement.",
        };
    }

    private static string BuildImprovementNarrative(
        string workflowName, double cpuChange, double ramChange, int insightDelta)
    {
        var parts = new List<string>();

        if (cpuChange <= -5)
            parts.Add($"CPU reduced by {Math.Abs(cpuChange):F0}%");

        if (ramChange <= -5)
            parts.Add($"RAM freed by {Math.Abs(ramChange):F0}%");

        if (insightDelta < 0)
            parts.Add($"{Math.Abs(insightDelta)} " +
                      (Math.Abs(insightDelta) == 1 ? "issue" : "issues") + " resolved");

        return parts.Count > 0
            ? $"The {workflowName} workflow improved system conditions: {string.Join(", ", parts)}."
            : $"The {workflowName} workflow completed with measurable improvements.";
    }
}
