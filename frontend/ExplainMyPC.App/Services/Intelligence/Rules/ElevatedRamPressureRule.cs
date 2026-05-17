using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Telemetry;

namespace ExplainMyPC.Services.Intelligence.Rules;

/// <summary>
/// Fires when RAM usage exceeds 85 % of total physical memory.
///
/// At this level Windows begins aggressively using the page file, which
/// degrades responsiveness even on fast SSDs. The finding refreshes every
/// tick so the free-GB figure stays accurate.
/// </summary>
public sealed class ElevatedRamPressureRule : IInsightRule
{
    private const double HighRamThreshold = 85.0;

    public string RuleId => "ram.high-pressure";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        // Skip until the sampler has populated total memory.
        if (current.RamTotalGb <= 0)
            return null;

        if (current.RamPercent <= HighRamThreshold)
            return null;

        double freeGb = current.RamTotalGb - current.RamUsedGb;

        return new SystemInsight(
            Id:         RuleId,
            Severity:   InsightSeverity.Recommendation,
            Title:      "High Memory Pressure",
            Detail:     $"Your PC is using {current.RamUsedGb:F1} GB of {current.RamTotalGb:F0} GB RAM " +
                        $"({current.RamPercent:0}%), leaving only {freeGb:F1} GB free. " +
                        $"Windows may start swapping data to disk, which can cause noticeable slowdowns.",
            ActionHint: "Close unused browser tabs or applications to free up memory.",
            DetectedAt: DateTimeOffset.Now);
    }
}
