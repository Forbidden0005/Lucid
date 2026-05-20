using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Telemetry;

namespace ExplainMyPC.Services.Intelligence.Rules;

/// <summary>
/// Fires when the primary disk is more than 90 % full.
/// Escalates to Warning when critically full (≥ 95 %).
///
/// Very low free space degrades NTFS performance, prevents Windows Update
/// from staging downloads, and can break application installs.
/// </summary>
public sealed class LowDiskSpaceRule : IInsightRule
{
    private const double LowThreshold      = 90.0;
    private const double CriticalThreshold = 95.0;

    // Allocated once at class load — no per-tick heap pressure.
    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.disk.clean-temp-files",
            Title:                "Clear Temp Files",
            Description:          "Windows and applications accumulate temporary files over time. Running Disk Cleanup removes safe-to-delete files and can recover several gigabytes.",
            Impact:               ActionImpact.Moderate,
            Effort:               ActionEffort.FewMinutes,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),

        new SystemAction(
            Id:                   "action.disk.run-storage-sense",
            Title:                "Run Storage Sense",
            Description:          "Storage Sense automatically removes downloaded Windows Update files, empties the Recycle Bin, and clears app caches to free significant disk space.",
            Impact:               ActionImpact.High,
            Effort:               ActionEffort.TenMinutes,
            Risk:                 ActionRisk.Low,
            RequiresConfirmation: true,
            IsReversible:         false,
            ConfirmationMessage:  "Storage Sense will permanently delete files in the Recycle Bin and downloaded update packages. These cannot be recovered."),
    ];

    public string RuleId => "disk.low-space";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        if (current.DiskTotalGb <= 0)
            return null;

        if (current.DiskPercent <= LowThreshold)
            return null;

        double freeGb     = current.DiskTotalGb - current.DiskUsedGb;
        bool   isCritical = current.DiskPercent >= CriticalThreshold;

        return new SystemInsight(
            Id:         RuleId,
            Severity:   isCritical ? InsightSeverity.Warning : InsightSeverity.Recommendation,
            Title:      isCritical ? "Disk Space Critically Low" : "Low Disk Space",
            Detail:     $"Your drive is {current.DiskPercent:0}% full — only {freeGb:F0} GB free of " +
                        $"{current.DiskTotalGb:F0} GB. " +
                        (isCritical
                            ? "At this level Windows may become unstable and updates could fail to install."
                            : "Low free space can slow down file operations and prevent software installations."),
            ActionHint:         "Use the Storage Cleanup tool to find and remove large or unnecessary files.",
            DetectedAt:         DateTimeOffset.Now,
            RecommendedActions: s_actions);
    }
}
