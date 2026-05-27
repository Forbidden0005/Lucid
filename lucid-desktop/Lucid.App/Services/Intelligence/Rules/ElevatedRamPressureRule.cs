using Lucid.Helpers;
using Lucid.Services.Telemetry;

namespace Lucid.Services.Intelligence.Rules;

/// <summary>
/// Fires when RAM usage exceeds 85 % of total physical memory.
///
/// Severity tiers:
///   85 %  → Recommendation — pressure beginning, Windows may start light paging.
///   90 %  → Warning — active paging occurring; system responsiveness is visibly
///            degraded; immediate action is needed to prevent a runaway cascade.
///
/// The finding refreshes every tick so the free-GB figure stays accurate.
/// Consistent with the two-tier design used by LowDiskSpaceRule and GpuVramPressureRule.
/// </summary>
public sealed class ElevatedRamPressureRule : IInsightRule
{
    private const double HighRamThreshold     = 85.0;
    private const double CriticalRamThreshold = 90.0;

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

        bool   isCritical  = current.RamPercent >= CriticalRamThreshold;
        double freeGb      = current.RamTotalGb - current.RamUsedGb;
        var    attributions = ProcessAttributionHelper.ForRam(current.TopProcesses, current.RamTotalGb);

        string detail = isCritical
            ? $"Your PC is using {current.RamUsedGb:F1} GB of {current.RamTotalGb:F0} GB RAM " +
              $"({current.RamPercent:0}%), with only {freeGb:F1} GB remaining. " +
              $"Windows is actively paging to disk at this level, which significantly " +
              $"degrades responsiveness — especially for open apps and file operations."
            : $"Your PC is using {current.RamUsedGb:F1} GB of {current.RamTotalGb:F0} GB RAM " +
              $"({current.RamPercent:0}%), leaving only {freeGb:F1} GB free. " +
              $"Windows may start swapping data to disk, which can cause noticeable slowdowns.";

        return new SystemInsight(
            Id:         RuleId,
            Severity:   isCritical ? InsightSeverity.Warning : InsightSeverity.Recommendation,
            Title:      isCritical ? "Memory Under Severe Pressure" : "High Memory Pressure",
            Detail:     detail,
            ActionHint:          isCritical
                                     ? "Free RAM immediately — Windows is actively paging to disk."
                                     : "Close unused browser tabs or applications to free up memory.",
            DetectedAt:          DateTimeOffset.Now,
            RecommendedActions:  s_actions,
            AttributedProcesses: attributions);
    }
}
