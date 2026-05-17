using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Telemetry;

namespace ExplainMyPC.Services.Intelligence.Rules;

/// <summary>
/// Produces an "all-clear" finding when every monitored metric is healthy.
///
/// This rule is intentionally placed last in SystemInsightEngine.CreateRules()
/// so it only fires after every other rule has had the opportunity to raise a
/// finding. If any other rule fires, the engine omits this one from the results
/// naturally because those other rules will have matching findings.
///
/// Wait requirement:
///   At least 5 history samples (~7.5 s) are required before this rule can
///   fire. This prevents a premature "all-clear" on cold start before
///   telemetry has had time to stabilise.
///
/// Thresholds (intentionally conservative — lower than the per-rule limits):
///   CPU    average  &lt; 70 %
///   RAM    current  &lt; 80 %
///   Disk   current  &lt; 85 %
///   CPU temp        &lt; 75 °C  (skipped if sensor unavailable)
///   GPU    current  &lt; 85 %  (skipped if GPU unavailable)
/// </summary>
public sealed class SystemRunningWellRule : IInsightRule
{
    private const double CpuOkThreshold  = 70.0;
    private const double RamOkThreshold  = 80.0;
    private const double DiskOkThreshold = 85.0;
    private const double TempOkThreshold = 75.0;
    private const double GpuOkThreshold  = 85.0;
    private const int    MinSamples      = 5;

    public string RuleId => "system.healthy";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        var cpuStats = history.GetStats(TelemetryMetric.Cpu, TimeWindow.LastMinute);

        // Require a minimum history depth before issuing an "all clear".
        if (cpuStats.IsEmpty || cpuStats.SampleCount < MinSamples)
            return null;

        // CPU must be comfortably below the sustained-high threshold.
        if (cpuStats.Average >= CpuOkThreshold)
            return null;

        // RAM must not be under pressure.
        if (current.RamPercent >= RamOkThreshold)
            return null;

        // Disk must not be getting full.
        if (current.DiskPercent >= DiskOkThreshold)
            return null;

        // Temperature must be within a safe range (sensor availability is optional).
        if (current.CpuTemperatureAvailable && current.CpuTemperatureCelsius >= TempOkThreshold)
            return null;

        // GPU must not be under unexpected load (GPU presence is optional).
        if (current.GpuAvailable && current.GpuPercent >= GpuOkThreshold)
            return null;

        return new SystemInsight(
            Id:         RuleId,
            Severity:   InsightSeverity.Info,
            Title:      "System Running Well",
            Detail:     $"All monitored components are within healthy ranges. " +
                        $"CPU is averaging {cpuStats.Average:0}%, RAM is at {current.RamPercent:0}%, " +
                        $"and disk usage is {current.DiskPercent:0}%.",
            ActionHint: null,
            DetectedAt: DateTimeOffset.Now);
    }
}
