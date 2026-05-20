using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Baseline;
using ExplainMyPC.Services.Telemetry;

namespace ExplainMyPC.Services.Intelligence.Rules;

/// <summary>
/// Fires when CPU usage is anomalously high relative to THIS machine's
/// learned idle baseline — even if it would not exceed the global 80 % threshold
/// used by <see cref="SustainedHighCpuRule"/>.
///
/// Example: a machine that normally idles at 4 % CPU is behaving unusually
/// at 30 %, even though 30 % would not trigger any fixed-threshold rule.
/// This rule detects that "30 % is not normal for YOU" using the learned baseline.
///
/// Thresholds:
///   Minimum absolute delta  ≥ 15 % above idle mean (avoids noise)
///   Z-score ≥ 2.0 → Recommendation
///   Z-score ≥ 3.0 → Warning
///
/// History:
///   Requires ≥ 10 CPU samples (~15 s) from the last minute to confirm
///   the elevation is sustained rather than a momentary spike.
///
/// Warm-up guard:
///   Returns null until <see cref="ISystemBaselineService.CurrentBaseline"/>
///   becomes non-null (≥ 200 baseline observations ≈ 5 min of learning).
///
/// ID: baseline.cpu-above-norm
/// </summary>
public sealed class BaselineCpuAnomalyRule : IInsightRule
{
    private const int    MinCpuSamples = 10;

    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.cpu.open-task-manager",
            Title:                "Open Task Manager",
            Description:          "Sort by CPU column to identify which process is causing your machine to work harder than usual.",
            Impact:               ActionImpact.Low,
            Effort:               ActionEffort.Seconds,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),
    ];

    private readonly ISystemBaselineService _baseline;

    public string RuleId => "baseline.cpu-above-norm";

    public BaselineCpuAnomalyRule(ISystemBaselineService baseline)
    {
        _baseline = baseline;
    }

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        // Guard: wait for the baseline warm-up period to complete.
        var baseline = _baseline.CurrentBaseline;
        if (baseline is null || !baseline.IdleCpuReady)
            return null;

        // Confirm the elevation is sustained (not a momentary spike).
        var cpuStats = history.GetStats(TelemetryMetric.Cpu, TimeWindow.LastMinute);
        if (cpuStats.IsEmpty || cpuStats.SampleCount < MinCpuSamples)
            return null;

        double cpuNow = current.CpuPercent;
        double zscore = baseline.IdleCpuZScore(cpuNow);

        if (!baseline.IsIdleCpuAnomaly(cpuNow))
            return null;

        bool    isWarning  = zscore >= MachineBaseline.CpuSigmaWarning;
        var     severity   = isWarning ? InsightSeverity.Warning : InsightSeverity.Recommendation;
        int     confidence = MachineBaseline.ConfidenceFromZScore(zscore);

        // Build a human-readable description of the normal range.
        double normalLow  = Math.Max(0, baseline.IdleCpuMean - baseline.IdleCpuStdDev);
        double normalHigh = baseline.IdleCpuMean + baseline.IdleCpuStdDev;

        string detail = isWarning
            ? $"Your CPU is running at {cpuNow:0}%, which is {zscore:F1} standard deviations above " +
              $"your machine's typical idle level of {baseline.IdleCpuMean:0}% " +
              $"(normal range: {normalLow:0}–{normalHigh:0}%). " +
              $"Something is consuming significantly more processor time than this machine usually does."
            : $"Your CPU is at {cpuNow:0}%, noticeably above your machine's normal idle level of " +
              $"{baseline.IdleCpuMean:0}% (normal range: {normalLow:0}–{normalHigh:0}%). " +
              $"This is higher than usual for this PC, even if it's within globally acceptable limits.";

        var attributions = ProcessAttributionHelper.ForCpu(current.TopProcesses);

        return new SystemInsight(
            Id:       RuleId,
            Severity: severity,
            Title:    isWarning
                          ? "CPU Significantly Above Your Normal"
                          : "CPU Higher Than Your Machine's Baseline",
            Detail:            detail,
            ActionHint:        "Open Task Manager to identify which process is driving the unusual CPU activity.",
            DetectedAt:        DateTimeOffset.Now,
            ConfidencePercent: confidence,
            RecommendedActions:  s_actions,
            AttributedProcesses: attributions);
    }
}
