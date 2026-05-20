using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Telemetry;

namespace ExplainMyPC.Services.Intelligence.Rules;

/// <summary>
/// Fires when GPU load is high while CPU load is low.
///
/// This combination is unusual during normal productivity work (documents,
/// email, browsing). When it persists it may indicate a background process —
/// a cryptocurrency miner, rogue video transcoder, or poorly optimised
/// browser renderer — is driving GPU activity.
///
/// Requires at least 20 samples (~30 s) before firing so a brief GPU burst
/// such as launching a game or a video-player splash screen is ignored.
/// </summary>
public sealed class AbnormalGpuUsageRule : IInsightRule
{
    private const double GpuHighThreshold = 35.0;
    private const double CpuLowThreshold  = 20.0;
    private const int    MinSamples       = 20;

    // Allocated once at class load — no per-tick heap pressure.
    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.gpu.inspect-processes",
            Title:                "Inspect GPU Processes",
            Description:          "Open Task Manager and navigate to the Performance → GPU tab to see which process is using the graphics card.",
            Impact:               ActionImpact.Low,
            Effort:               ActionEffort.Seconds,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),

        new SystemAction(
            Id:                   "action.gpu.disable-browser-hwaccel",
            Title:                "Disable Browser GPU",
            Description:          "Disabling hardware acceleration in your browser's settings removes a common source of unexpected background GPU usage.",
            Impact:               ActionImpact.Moderate,
            Effort:               ActionEffort.FewMinutes,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),
    ];

    public string RuleId => "gpu.unexpected-load";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        if (!current.GpuAvailable)
            return null;

        var gpuStats = history.GetStats(TelemetryMetric.Gpu, TimeWindow.LastFiveMinutes);
        var cpuStats = history.GetStats(TelemetryMetric.Cpu, TimeWindow.LastFiveMinutes);

        if (gpuStats.IsEmpty || gpuStats.SampleCount < MinSamples)
            return null;
        if (cpuStats.IsEmpty)
            return null;

        if (gpuStats.Average <= GpuHighThreshold || cpuStats.Average >= CpuLowThreshold)
            return null;

        // 5-minute window ≈ 200 samples at the 1.5-second poll rate.
        int confidence = Math.Clamp(gpuStats.SampleCount * 100 / 200, 72, 95);

        var attributions = ProcessAttributionHelper.ForGpu(current.TopProcesses);

        return new SystemInsight(
            Id:                RuleId,
            Severity:          InsightSeverity.Warning,
            Title:             "Unexpected GPU Activity",
            Detail:            $"Your GPU has averaged {gpuStats.Average:0}% load over the last 5 minutes " +
                               $"while CPU usage remained low ({cpuStats.Average:0}%). " +
                               $"This pattern is uncommon during typical productivity tasks and may indicate " +
                               $"a background process is using the graphics card.",
            ActionHint:          "Open Task Manager → Performance → GPU to identify which process is responsible.",
            DetectedAt:          DateTimeOffset.Now,
            ConfidencePercent:   confidence,
            RecommendedActions:  s_actions,
            AttributedProcesses: attributions);
    }
}
