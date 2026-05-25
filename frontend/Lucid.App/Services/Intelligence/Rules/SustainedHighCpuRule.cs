using Lucid.Helpers;
using Lucid.Services.Telemetry;

namespace Lucid.Services.Intelligence.Rules;

/// <summary>
/// Fires when the 1-minute average CPU load exceeds 80 %.
///
/// Sustained high CPU is the most common cause of a "sluggish PC" complaint.
/// A single-sample spike is not enough — the rule waits for at least 10 samples
/// (~15 s) so it doesn't fire during a brief burst like launching an app.
/// </summary>
public sealed class SustainedHighCpuRule : IInsightRule
{
    private const double HighCpuThreshold = 80.0;
    private const int    MinSamples       = 10;

    // Allocated once at class load — no per-tick heap pressure.
    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.cpu.open-task-manager",
            Title:                "Open Task Manager",
            Description:          "Sort processes by CPU column to identify which application is consuming the most processor time.",
            Impact:               ActionImpact.High,
            Effort:               ActionEffort.Seconds,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),
    ];

    public string RuleId => "cpu.sustained-high";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        var stats = history.GetStats(TelemetryMetric.Cpu, TimeWindow.LastMinute);
        if (stats.IsEmpty || stats.SampleCount < MinSamples)
            return null;

        if (stats.Average <= HighCpuThreshold)
            return null;

        // Confidence scales from 72 % at MinSamples to 98 % at a full 1-minute window
        // (~40 samples at the 1.5-second poll rate).
        int confidence = Math.Clamp(stats.SampleCount * 100 / 40, 72, 98);

        var attributions = ProcessAttributionHelper.ForCpu(current.TopProcesses);

        return new SystemInsight(
            Id:                RuleId,
            Severity:          InsightSeverity.Warning,
            Title:             "CPU Under Heavy Load",
            Detail:            $"Your processor has averaged {stats.Average:0}% usage over the last minute " +
                               $"(currently {current.CpuPercent:0}%). Sustained high CPU usage can make " +
                               $"your PC feel unresponsive and slow to switch between tasks.",
            ActionHint:          "Open Task Manager (Ctrl+Shift+Esc) and sort by CPU to find the responsible process.",
            DetectedAt:          DateTimeOffset.Now,
            ConfidencePercent:   confidence,
            RecommendedActions:  s_actions,
            AttributedProcesses: attributions);
    }
}
