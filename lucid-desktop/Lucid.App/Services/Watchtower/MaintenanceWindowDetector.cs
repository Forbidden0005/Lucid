using Lucid.Services.Analytics;
using Lucid.Services.Governance;

namespace Lucid.Services.Watchtower;

/// <summary>
/// Suggests maintenance windows based on governance mode, workload patterns,
/// and historical data coverage.
///
/// The detector NEVER schedules automatically — it only suggests.
/// All suggestions are reversible (the user decides when to act).
/// </summary>
public static class MaintenanceWindowDetector
{
    /// <summary>
    /// Builds a list of maintenance window recommendations based on the current
    /// governance mode and historical summary.
    /// </summary>
    public static IReadOnlyList<MaintenanceWindowRecommendation> GetRecommendations(
        RuntimeMode      currentMode,
        HistoricalSummary summary)
    {
        var recs = new List<MaintenanceWindowRecommendation>();

        // ── 1. Disk cleanup suggestion ────────────────────────────────────────
        // Suggest when disk trend is worsening, or health score is low
        if (summary.DiskTrend.Direction == TrendDirection.Worsening
            || summary.SevenDayHealth.Score < 75)
        {
            var priority = summary.DiskTrend.ChangePercent >= 15.0
                ? MaintenancePriority.Timely
                : MaintenancePriority.Recommended;

            recs.Add(new MaintenanceWindowRecommendation(
                WindowId:                 "maint.disk-cleanup",
                Title:                    "Disk cleanup",
                Rationale:                summary.DiskTrend.Direction == TrendDirection.Worsening
                    ? $"Disk usage has trended upward recently ({summary.DiskTrend.TrendLabel}). " +
                      "A cleanup pass may help recover space."
                    : "A routine disk cleanup may help maintain available space.",
                SuggestedTimeDescription: IdleModeDescription(currentMode),
                Priority:                 priority,
                SuggestedActionKeys: [
                    "action.disk.clean-temp-files",
                    "action.disk.empty-recycle-bin",
                    "action.disk.clean-windows-update-cache",
                ]));
        }

        // ── 2. SFC / DISM health scan ─────────────────────────────────────────
        // Suggest when health score has been low for a while (low 30-day score)
        if (summary.ThirtyDayHealth.Score < 70 && summary.ThirtyDayHealth.WarningCount >= 5)
        {
            recs.Add(new MaintenanceWindowRecommendation(
                WindowId:                 "maint.system-health-scan",
                Title:                    "Windows system file health check",
                Rationale:                "Frequent system events over the past month may indicate " +
                                          "underlying Windows component issues. A system file scan " +
                                          "can rule out file corruption.",
                SuggestedTimeDescription: "When the system is next idle and on AC power",
                Priority:                 MaintenancePriority.Recommended,
                SuggestedActionKeys: [
                    "action.repair.sfc-scannow",
                    "action.repair.dism-restore-health",
                ]));
        }

        // ── 3. Startup review suggestion ──────────────────────────────────────
        // Suggest when startup-related patterns are recurring
        var startupPatterns = summary.RecurringPatterns
            .Where(p => p.InsightId.Contains("startup", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (startupPatterns.Count > 0)
        {
            var pattern = startupPatterns[0];
            recs.Add(new MaintenanceWindowRecommendation(
                WindowId:                 "maint.startup-review",
                Title:                    "Startup items review",
                Rationale:                $"The \"{pattern.Title}\" pattern has been seen {pattern.TotalOccurrences} " +
                                          $"times ({pattern.FrequencyLabel}). Reviewing startup items may reduce " +
                                          "its frequency.",
                SuggestedTimeDescription: "Any time — reviewing startup items is quick",
                Priority:                 MaintenancePriority.Routine,
                SuggestedActionKeys: [
                    "action.startup.open-startup-apps",
                ]));
        }

        // ── 4. Routine maintenance (low-urgency, always suggested) ───────────
        // Suggest browser cache cleanup as routine if no specific disk pressure
        if (!recs.Any(r => r.WindowId == "maint.disk-cleanup"))
        {
            recs.Add(new MaintenanceWindowRecommendation(
                WindowId:                 "maint.routine",
                Title:                    "Routine maintenance",
                Rationale:                "Periodic light cleanup helps keep the system running smoothly. " +
                                          "No urgent issues have been detected.",
                SuggestedTimeDescription: IdleModeDescription(currentMode),
                Priority:                 MaintenancePriority.Routine,
                SuggestedActionKeys: [
                    "action.disk.clean-temp-files",
                    "action.disk.clean-browser-cache",
                ]));
        }

        // Sort by priority (Timely first)
        recs.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        return recs;
    }

    private static string IdleModeDescription(RuntimeMode mode) => mode switch
    {
        RuntimeMode.Normal   => "The system appears idle now — a good time to run cleanup",
        RuntimeMode.LowPower => "When connected to AC power and the system is idle",
        RuntimeMode.Gaming   => "When not gaming — gaming sessions reduce background capacity",
        _                    => "When the system is next idle and not under heavy load",
    };
}
