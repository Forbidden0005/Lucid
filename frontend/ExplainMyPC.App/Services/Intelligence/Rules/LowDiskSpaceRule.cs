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
            ActionHint: "Use the Storage Cleanup tool to find and remove large or unnecessary files.",
            DetectedAt: DateTimeOffset.Now);
    }
}
