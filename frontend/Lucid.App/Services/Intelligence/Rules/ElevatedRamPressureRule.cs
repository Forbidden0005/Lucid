using Lucid.Helpers;
using Lucid.Services.Telemetry;

namespace Lucid.Services.Intelligence.Rules;

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

    // Allocated once at class load — no per-tick heap pressure.
    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.ram.close-browser-tabs",
            Title:                "Close Browser Tabs",
            Description:          "Each open browser tab holds memory. Closing unused tabs is the fastest way to free RAM with no risk.",
            Impact:               ActionImpact.Moderate,
            Effort:               ActionEffort.Seconds,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),

        new SystemAction(
            Id:                   "action.ram.restart-heavy-apps",
            Title:                "Restart Heavy Apps",
            Description:          "Applications like browsers and IDEs can accumulate memory over time. Restarting them reclaims leaked memory.",
            Impact:               ActionImpact.High,
            Effort:               ActionEffort.FewMinutes,
            Risk:                 ActionRisk.Low,
            RequiresConfirmation: true,
            IsReversible:         true,
            ConfirmationMessage:  "Restarting an app will close its current windows. Save your work before proceeding."),
    ];

    public string RuleId => "ram.high-pressure";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        // Skip until the sampler has populated total memory.
        if (current.RamTotalGb <= 0)
            return null;

        if (current.RamPercent <= HighRamThreshold)
            return null;

        double freeGb      = current.RamTotalGb - current.RamUsedGb;
        var    attributions = ProcessAttributionHelper.ForRam(current.TopProcesses, current.RamTotalGb);

        return new SystemInsight(
            Id:         RuleId,
            Severity:   InsightSeverity.Recommendation,
            Title:      "High Memory Pressure",
            Detail:     $"Your PC is using {current.RamUsedGb:F1} GB of {current.RamTotalGb:F0} GB RAM " +
                        $"({current.RamPercent:0}%), leaving only {freeGb:F1} GB free. " +
                        $"Windows may start swapping data to disk, which can cause noticeable slowdowns.",
            ActionHint:          "Close unused browser tabs or applications to free up memory.",
            DetectedAt:          DateTimeOffset.Now,
            RecommendedActions:  s_actions,
            AttributedProcesses: attributions);
    }
}
