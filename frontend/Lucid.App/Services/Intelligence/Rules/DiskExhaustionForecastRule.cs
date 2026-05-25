using Lucid.Helpers;
using Lucid.Services.Intelligence.Forecasting;
using Lucid.Services.Telemetry;

namespace Lucid.Services.Intelligence.Rules;

/// <summary>
/// Predicts when disk space will be exhausted, given the current usage trajectory.
///
/// Two detection paths address different scenarios:
///
///   Path A — Slope-based (slow drift near capacity):
///     Uses least-squares regression over the 30-minute disk% history.
///     Fires when disk is ≥ 78 % full AND the slope projects exhaustion
///     within 48 hours. Catches gradual disk fill from downloads, virtual
///     machine expansion, log accumulation, and similar sustained-write
///     workloads. Most reliable on high-capacity disks near their limit.
///
///   Path B — Write-rate-based (active large transfer):
///     Uses the current disk write rate (MB/s) to estimate hours to full.
///     Fires when disk is ≥ 65 % full AND sustained disk I/O (confirmed by
///     the 5-minute disk% average) suggests exhaustion within 4 hours.
///     Catches large file copies, OS downloads, and archive operations
///     that would fill the disk within the current work session.
///
/// Both paths avoid overlap with <see cref="LowDiskSpaceRule"/>:
///   That rule fires reactively when disk is already ≥ 90 % full.
///   This rule fires proactively — before that threshold is reached.
///
/// Severity:
///   Warning        — exhaustion projected within 4 hours (Path B) or 12 hours (Path A)
///   Recommendation — exhaustion projected within 48 hours (Path A only)
///
/// ID: forecast.disk-exhaustion
/// </summary>
public sealed class DiskExhaustionForecastRule : IInsightRule
{
    // ── Path A (slope-based) thresholds ───────────────────────────────────────
    private const double PathA_MinDiskPct        = 78.0;   // disk must be nearing full
    private const double PathA_MinSlopePctPerMin = 0.004;  // ~2.4%/hour — detectable growth
    private const double PathA_WarningHours      = 12.0;
    private const double PathA_MaxHours          = 48.0;

    // ── Path B (write-rate-based) thresholds ──────────────────────────────────
    private const double PathB_MinDiskPct        = 65.0;   // less-full disk with active write
    private const double PathB_MinWriteMbps      = 8.0;    // meaningful sustained write rate
    private const int    PathB_MinDiskSamples    = 20;     // ≥ 30 s of disk% history
    private const double PathB_ElevatedDiskPct   = 30.0;   // 5-min avg must also be elevated
    private const double PathB_WarningHours      = 4.0;

    // ── Minimum samples for slope computation ─────────────────────────────────
    private const int MinSamplesForSlope = 80;             // ≈ 2 min at 1.5 s/tick

    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.disk.clean-temp-files",
            Title:                "Clear Temp Files",
            Description:          "Removing temporary files can recover multiple gigabytes and extend the time before your disk reaches capacity.",
            Impact:               ActionImpact.Moderate,
            Effort:               ActionEffort.FewMinutes,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),

        new SystemAction(
            Id:                   "action.disk.run-storage-sense",
            Title:                "Run Storage Sense",
            Description:          "Storage Sense removes Windows Update caches, empties the Recycle Bin, and clears application caches to free significant disk space.",
            Impact:               ActionImpact.High,
            Effort:               ActionEffort.TenMinutes,
            Risk:                 ActionRisk.Low,
            RequiresConfirmation: true,
            IsReversible:         false,
            ConfirmationMessage:  "Storage Sense will permanently delete files in the Recycle Bin and downloaded update packages. These cannot be recovered."),
    ];

    public string RuleId => "forecast.disk-exhaustion";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        if (current.DiskTotalGb <= 0) return null;

        double freeGb     = current.DiskTotalGb - current.DiskUsedGb;
        double diskPct    = current.DiskPercent;

        // ── Path A: slope-based forecast ──────────────────────────────────────
        if (diskPct >= PathA_MinDiskPct)
        {
            var stats30m = history.GetStats(TelemetryMetric.Disk, TimeWindow.LastThirtyMinutes);

            if (!stats30m.IsEmpty && stats30m.SampleCount >= MinSamplesForSlope)
            {
                var    samples = history.GetSamples(TelemetryMetric.Disk, TimeWindow.LastThirtyMinutes);
                double slope   = TrendAnalysis.SlopePerMinute(samples);

                if (slope >= PathA_MinSlopePctPerMin)
                {
                    double hoursToFull = ForecastHelper.HoursUntilDiskThreshold(diskPct, slope);

                    if (hoursToFull <= PathA_MaxHours)
                    {
                        bool   isWarning  = hoursToFull <= PathA_WarningHours;
                        int    confidence = ForecastHelper.ForecastConfidence(
                                               stats30m.SampleCount,
                                               TimeWindow.LastThirtyMinutes,
                                               hoursToFull * 60);

                        return BuildInsight(
                            severity:        isWarning ? InsightSeverity.Warning : InsightSeverity.Recommendation,
                            confidence:      confidence,
                            diskPct:         diskPct,
                            freeGb:          freeGb,
                            totalGb:         current.DiskTotalGb,
                            timeDescription: ForecastHelper.FormatHourHorizon(hoursToFull),
                            slopeNote:       $"Disk usage is growing at {slope * 60:F1} %/hour.",
                            isWarning:       isWarning);
                    }
                }
            }
        }

        // ── Path B: write-rate-based forecast ─────────────────────────────────
        if (diskPct >= PathB_MinDiskPct && current.DiskWriteMbps >= PathB_MinWriteMbps)
        {
            // Confirm the write rate is genuinely sustained, not a momentary burst.
            // The 5-minute disk% average must also be elevated (not idle).
            var stats5m = history.GetStats(TelemetryMetric.Disk, TimeWindow.LastFiveMinutes);
            if (!stats5m.IsEmpty
                && stats5m.SampleCount >= PathB_MinDiskSamples
                && stats5m.Average >= PathB_ElevatedDiskPct)
            {
                double hoursToFull = ForecastHelper.HoursUntilDiskFullFromWriteRate(
                                         freeGb, current.DiskWriteMbps);

                if (hoursToFull is > 0 and <= PathB_WarningHours)
                {
                    int confidence = ForecastHelper.ForecastConfidence(
                                         stats5m.SampleCount,
                                         TimeWindow.LastFiveMinutes,
                                         hoursToFull * 60,
                                         minConf: 60, maxConf: 80);

                    return BuildInsight(
                        severity:        InsightSeverity.Warning,
                        confidence:      confidence,
                        diskPct:         diskPct,
                        freeGb:          freeGb,
                        totalGb:         current.DiskTotalGb,
                        timeDescription: ForecastHelper.FormatHourHorizon(hoursToFull),
                        slopeNote:       $"Current write rate: {current.DiskWriteMbps:F0} MB/s.",
                        isWarning:       true);
                }
            }
        }

        return null;
    }

    // ── Builder ───────────────────────────────────────────────────────────────

    private SystemInsight BuildInsight(
        InsightSeverity severity,
        int             confidence,
        double          diskPct,
        double          freeGb,
        double          totalGb,
        string          timeDescription,
        string          slopeNote,
        bool            isWarning)
    {
        string title = isWarning
            ? "Disk Space Running Out Soon"
            : "Disk on Track to Fill Up";

        string detail = $"Your disk is {diskPct:0}% full with {freeGb:F1} GB remaining of {totalGb:F0} GB. " +
                        $"{slopeNote} " +
                        $"At this rate, the disk could reach capacity in {timeDescription}. " +
                        (isWarning
                            ? "When a disk runs out of space, Windows becomes unstable, updates fail, and applications cannot save files."
                            : "Cleaning up unnecessary files now can prevent a capacity crisis later.");

        return new SystemInsight(
            Id:                RuleId,
            Severity:          severity,
            Title:             title,
            Detail:            detail,
            ActionHint:        "Free up disk space now before available capacity becomes critically low.",
            DetectedAt:        DateTimeOffset.Now,
            ConfidencePercent: confidence,
            RecommendedActions: s_actions);
    }
}
