using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Telemetry;

namespace ExplainMyPC.Services.Intelligence.Rules;

/// <summary>
/// Fires when disk utilisation has been consistently elevated for an extended period.
///
/// This rule is distinct from <see cref="HighDiskThroughputRule"/>:
///   • High throughput: disk I/O rate (MB/s) is very high right now — a burst condition.
///   • Long-running pressure: disk utilisation (%) has stayed elevated for many minutes —
///     a sustained background operation such as a full antivirus scan, a large file transfer,
///     Windows Update downloading and staging, or a search indexing pass.
///
/// A long-running disk operation is often benign, but it competes with foreground I/O
/// and can cause perceptible application latency on HDDs and entry-level SSDs.
///
/// Detection method:
///   The 30-minute average disk utilisation must be above the threshold and must
///   have been present for long enough (≥ 300 samples ≈ 7.5 minutes) to confirm
///   it is truly sustained and not just the tail of a completed operation.
///   The 1-minute average must also be elevated, confirming the pressure is still active.
///
/// Confidence:
///   Scales from 65 % at minimum data to 90 % at full 30-minute window coverage.
/// </summary>
public sealed class LongRunningDiskPressureRule : IInsightRule
{
    // 30-min average must be above this level to flag sustained pressure
    private const double LongAvgThreshold    = 45.0;
    // 1-min average must remain elevated — confirms pressure is still active now
    private const double RecentAvgThreshold  = 35.0;
    // Minimum samples in the 30-min window before the rule can fire (≈ 7.5 min)
    private const int    MinLongSamples      = 300;

    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.cpu.open-task-manager",
            Title:                "Identify Disk Activity in Task Manager",
            Description:          "Open Task Manager and click the Disk column to sort processes by disk usage. The process responsible for sustained I/O will be near the top.",
            Impact:               ActionImpact.Low,
            Effort:               ActionEffort.Seconds,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),

        new SystemAction(
            Id:                   "action.disk.run-storage-sense",
            Title:                "Run Storage Sense",
            Description:          "If disk pressure is caused by Windows Update staging or cache accumulation, Storage Sense can reclaim the space that is driving the I/O.",
            Impact:               ActionImpact.Moderate,
            Effort:               ActionEffort.TenMinutes,
            Risk:                 ActionRisk.Low,
            RequiresConfirmation: true,
            IsReversible:         false,
            ConfirmationMessage:  "Storage Sense will permanently delete files in the Recycle Bin and downloaded update packages."),
    ];

    public string RuleId => "disk.long-running-pressure";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        // Gate 1: require a meaningful amount of history
        var stats1m  = history.GetStats(TelemetryMetric.Disk, TimeWindow.LastMinute);
        var stats30m = history.GetStats(TelemetryMetric.Disk, TimeWindow.LastThirtyMinutes);

        if (stats30m.IsEmpty || stats30m.SampleCount < MinLongSamples) return null;

        // Gate 2: the long window average must be genuinely elevated
        if (stats30m.Average < LongAvgThreshold) return null;

        // Gate 3: disk pressure must still be active right now (not a completed scan)
        if (stats1m.IsEmpty || stats1m.Average < RecentAvgThreshold) return null;

        int    confidence = TrendAnalysis.ConfidenceFromCoverage(stats30m.SampleCount,
                                TimeWindow.LastThirtyMinutes, min: 65, max: 90);
        string elapsed    = TrendAnalysis.FormatElapsed(stats30m.SampleCount);

        // Add context about disk throughput if available
        string throughputNote = (current.DiskReadMbps + current.DiskWriteMbps) > 5
            ? $" Current throughput: {current.DiskReadMbps:F0} MB/s read, {current.DiskWriteMbps:F0} MB/s write."
            : string.Empty;

        return new SystemInsight(
            Id:       RuleId,
            Severity: InsightSeverity.Recommendation,
            Title:    "Sustained Disk Activity Detected",
            Detail:   $"Disk utilisation has averaged {stats30m.Average:0}% over the last {elapsed} " +
                      $"(currently {current.DiskPercent:0}%).{throughputNote} " +
                      $"Prolonged disk activity is often caused by a background operation — " +
                      $"such as a Windows Update download, an antivirus scan, or file " +
                      $"indexing — and typically completes on its own. On an HDD or a slow " +
                      $"SSD, it can cause noticeable application latency while it runs.",
            ActionHint:        "Open Task Manager and sort by Disk to identify the background process responsible.",
            DetectedAt:        DateTimeOffset.Now,
            ConfidencePercent: confidence,
            RecommendedActions: s_actions);
    }
}
