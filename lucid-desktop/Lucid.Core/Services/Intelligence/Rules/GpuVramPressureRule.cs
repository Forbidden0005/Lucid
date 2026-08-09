using Lucid.Helpers;

namespace Lucid.Services.Intelligence.Rules;

/// <summary>
/// Fires when GPU VRAM utilisation exceeds 85 % of installed capacity.
///
/// When VRAM fills up, the GPU begins paging texture data to system RAM —
/// a process significantly slower than dedicated video memory — causing
/// visible stuttering, frame-rate drops, and occasional driver crashes in
/// graphics-intensive applications (games, video editors, AI tools).
///
/// Threshold design:
///   85 %  → Recommendation — pressure beginning, room to act before impact.
///   95 %  → Warning — paging is actively occurring, immediate action needed.
///
/// Note:
///   VRAM is tracked as a point-in-time value in TelemetrySnapshot, not in
///   the rolling history buffer, so this rule reads the current snapshot
///   directly. Single-sample fire is appropriate: VRAM pressure at 85 %+
///   is immediately actionable regardless of how long it has been sustained.
///
/// Guards:
///   • Silent when GpuAvailable = false (integrated graphics or no GPU).
///   • Silent when GpuVramTotalGb = 0 (GPU Adapter Memory counter not populated).
///
/// ID: gpu.vram-pressure
/// </summary>
public sealed class GpuVramPressureRule : IInsightRule
{
    private const double HighThreshold     = 85.0;  // % — paging begins, recommend action
    private const double CriticalThreshold = 95.0;  // % — severe paging, immediate warning

    // Allocated once at class load — no per-tick heap pressure.
    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.gpu.close-unused-gpu-apps",
            Title:                "Close Unused GPU Applications",
            Description:          "Video editors, 3D modellers, games, and AI tools hold VRAM even when idle. Closing the ones you are not actively using will immediately free video memory.",
            Impact:               ActionImpact.High,
            Effort:               ActionEffort.Seconds,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),

        new SystemAction(
            Id:                   "action.gpu.disable-browser-hwaccel",
            Title:                "Disable Browser Hardware Acceleration",
            Description:          "Browsers with hardware acceleration enabled reserve a VRAM allocation even when idle. Disabling it in your browser's advanced settings frees that reservation.",
            Impact:               ActionImpact.Moderate,
            Effort:               ActionEffort.FewMinutes,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),
    ];

    public string RuleId => "gpu.vram-pressure";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        // Requires a discrete GPU with an active VRAM counter.
        if (!current.GpuAvailable || current.GpuVramTotalGb <= 0)
            return null;

        double vramPct = current.GpuVramUsedGb / current.GpuVramTotalGb * 100.0;

        if (vramPct <= HighThreshold)
            return null;

        bool   isCritical = vramPct >= CriticalThreshold;
        double freeGb     = current.GpuVramTotalGb - current.GpuVramUsedGb;

        string detail = isCritical
            ? $"Your GPU has only {freeGb:F1} GB of VRAM remaining " +
              $"({current.GpuVramUsedGb:F1} GB used of {current.GpuVramTotalGb:F0} GB, {vramPct:0}% full). " +
              $"VRAM is nearly exhausted and the GPU is actively paging to system RAM. " +
              $"This causes severe stuttering and may crash graphics-intensive applications."
            : $"Your GPU is using {current.GpuVramUsedGb:F1} GB of " +
              $"{current.GpuVramTotalGb:F0} GB VRAM ({vramPct:0}% full, {freeGb:F1} GB remaining). " +
              $"When VRAM fills up, the GPU begins using slower system RAM as overflow, " +
              $"causing stuttering and frame drops in games, video editors, and AI tools.";

        return new SystemInsight(
            Id:                 RuleId,
            Severity:           isCritical ? InsightSeverity.Warning : InsightSeverity.Recommendation,
            Title:              isCritical ? "GPU Memory Near Capacity" : "GPU Memory Running Low",
            Detail:             detail,
            ActionHint:         "Close games, video editors, or AI tools you are not actively using to free VRAM.",
            DetectedAt:         DateTimeOffset.Now,
            RecommendedActions: s_actions);
    }
}
