using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Telemetry;

namespace ExplainMyPC.Services.Intelligence.Rules;

/// <summary>
/// Fires when CPU usage has been consistently escalating over time.
///
/// This rule is complementary to <see cref="SustainedHighCpuRule"/>:
///   • <see cref="SustainedHighCpuRule"/> fires when CPU is already high (> 80 %),
///     capturing the "CPU is at the limit now" condition.
///   • <see cref="CpuEscalationRule"/> fires when CPU is trending upward over
///     a longer timeframe, potentially before the threshold is reached — capturing
///     the "something is gradually consuming more CPU" condition.
///
/// Detection method:
///   Two-tier window delta: the 5-minute average must be substantially higher
///   than the 30-minute average (long-term escalation), AND the regression slope
///   must confirm the series is genuinely ascending rather than plateauing.
///
/// Minimum data requirements:
///   ≥ 80 samples in the 30-minute window (≈ 2 minutes of data).
///
/// Confidence:
///   Scales from 65 % at minimum data to 90 % at full 30-minute coverage.
/// </summary>
public sealed class CpuEscalationRule : IInsightRule
{
    // CPU % floor below which we don't flag escalation (idle noise)
    private const double BaselineFloor       = 25.0;
    // 5-min avg must exceed 30-min avg by at least this many points
    private const double MinWindowDelta      = 15.0;
    // Regression slope must be at least this positive
    private const double MinSlopePerMinute   = 0.50;
    // Minimum 30-min samples before the rule can fire
    private const int    MinLongSamples      = 80;

    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.cpu.open-task-manager",
            Title:                "Open Task Manager",
            Description:          "Sort processes by CPU column to identify which application has been growing in CPU usage over the last several minutes.",
            Impact:               ActionImpact.High,
            Effort:               ActionEffort.Seconds,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),
    ];

    public string RuleId => "cpu.escalating";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        // Gate 1: CPU must be above idle noise
        if (current.CpuPercent < BaselineFloor) return null;

        // Gate 2: require sufficient history
        var stats5m  = history.GetStats(TelemetryMetric.Cpu, TimeWindow.LastFiveMinutes);
        var stats30m = history.GetStats(TelemetryMetric.Cpu, TimeWindow.LastThirtyMinutes);

        if (stats5m.IsEmpty || stats30m.IsEmpty || stats30m.SampleCount < MinLongSamples)
            return null;

        // Gate 3: the recent window must be substantially higher than the long window
        double delta = stats5m.Average - stats30m.Average;
        if (delta < MinWindowDelta) return null;

        // Gate 4: confirm via regression that the series is genuinely ascending
        var    samples = history.GetSamples(TelemetryMetric.Cpu, TimeWindow.LastThirtyMinutes);
        double slope   = TrendAnalysis.SlopePerMinute(samples);
        if (slope < MinSlopePerMinute) return null;

        int    confidence = TrendAnalysis.ConfidenceFromCoverage(stats30m.SampleCount,
                                TimeWindow.LastThirtyMinutes, min: 65, max: 90);
        string elapsed    = TrendAnalysis.FormatElapsed(stats30m.SampleCount);

        // Format slope for the detail text: "~0.8% per minute"
        string slopeText  = $"~{slope:F1}% per minute";

        var attributions = ProcessAttributionHelper.ForCpu(current.TopProcesses);

        return new SystemInsight(
            Id:       RuleId,
            Severity: InsightSeverity.Warning,
            Title:    "CPU Usage Is Escalating",
            Detail:   $"Processor usage has risen from an average of {stats30m.Average:0}% " +
                      $"to {stats5m.Average:0}% over the last {elapsed} " +
                      $"(currently {current.CpuPercent:0}%, climbing at {slopeText}). " +
                      $"Gradual escalation often indicates a process that accumulates work " +
                      $"over time — such as a runaway background task, a memory leak " +
                      $"causing swapping overhead, or an application entering a busy loop.",
            ActionHint:          "Open Task Manager and sort by CPU to identify the process responsible for the upward trend.",
            DetectedAt:          DateTimeOffset.Now,
            ConfidencePercent:   confidence,
            RecommendedActions:  s_actions,
            AttributedProcesses: attributions);
    }
}
