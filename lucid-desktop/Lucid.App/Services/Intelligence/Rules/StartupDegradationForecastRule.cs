using Lucid.Helpers;
using Lucid.Services.Baseline;
using Lucid.Services.Intelligence.Forecasting;
using Lucid.Services.Startup;

namespace Lucid.Services.Intelligence.Rules;

/// <summary>
/// Estimates how long the machine will take to settle after the next Windows login,
/// based on the composition of the current startup application list.
///
/// Complementary rules — how they differ:
///   <see cref="StartupItemCountRule"/>           — reactive: fires on raw entry count (≥ 8).
///   <see cref="HighImpactStartupAppsRule"/>       — reactive: flags specific heavy entries.
///   <see cref="StartupResourceCorrelationRule"/>  — reactive: correlates startup with live pressure.
///   This rule                                    — predictive: estimates future post-login
///                                                  settle time from startup composition.
///
/// Model (from <see cref="ForecastHelper.EstimateStartupSettlingMinutes"/>):
///   High-impact app   (Electron / browser / launcher) : +2.0 min each
///   Moderate-impact app (sync client / peripheral mgr): +0.8 min each
///   Any other startup entry                           : +0.2 min each
///   Capped at 25 min to prevent absurdly large estimates.
///
/// Horizon gates:
///   Fires when the estimated settle time is ≥ 4 minutes.
///   Severity escalates to Warning when the estimate exceeds 10 minutes.
///
/// Baseline comparison (optional):
///   When a baseline service is provided and has observed enough data, the rule
///   also compares the current startup count against the baseline snapshot to
///   detect when the startup list has grown noticeably since it was last measured.
///
/// Warm-up:
///   Silent until the first StartupSampler refresh cycle completes (~60 s).
///
/// ID: forecast.startup-degradation
/// </summary>
public sealed class StartupDegradationForecastRule : IInsightRule
{
    private const double WarningMinutes     = 10.0;   // ≥ 10 min estimate → Warning
    private const double MinActionMinutes   = 4.0;    // below this → not worth surfacing
    private const int    BaselineGrowthGate = 3;      // ≥ 3 more entries than baseline → mention growth

    private readonly ISystemBaselineService? _baseline;

    public StartupDegradationForecastRule(ISystemBaselineService? baseline = null)
    {
        _baseline = baseline;
    }

    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.startup.open-startup-apps",
            Title:                "Review Startup Apps",
            Description:          "Open Windows Startup Apps settings to disable applications that don't need to launch at sign-in. Each high-impact entry you remove can shave 1–2 minutes off your post-login settling time.",
            Impact:               ActionImpact.High,
            Effort:               ActionEffort.FewMinutes,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),
    ];

    public string RuleId => "forecast.startup-degradation";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        // Wait until the first StartupSampler refresh cycle completes.
        if (current.StartupEntries.Count == 0)
            return null;

        // Classify startup entries by impact level.
        int highImpact     = 0;
        int moderateImpact = 0;

        foreach (var entry in current.StartupEntries)
        {
            if (entry.Impact == StartupImpact.High)
                highImpact++;
            else if (entry.Impact == StartupImpact.Moderate)
                moderateImpact++;
        }

        int total = current.StartupEntries.Count;

        double estimatedMinutes = ForecastHelper.EstimateStartupSettlingMinutes(
            highImpact, moderateImpact, total);

        if (estimatedMinutes < MinActionMinutes)
            return null;

        bool isWarning = estimatedMinutes >= WarningMinutes;

        // Optional: detect startup list growth relative to the stored baseline.
        string growthNote = BuildGrowthNote(total);

        // Build the body sentence about which categories are contributing.
        string contributorNote = BuildContributorNote(highImpact, moderateImpact, total);

        string timeDesc = ForecastHelper.FormatMinuteHorizon(estimatedMinutes);

        return new SystemInsight(
            Id:       RuleId,
            Severity: isWarning ? InsightSeverity.Warning : InsightSeverity.Recommendation,
            Title:    isWarning
                          ? "Next Login Will Be Slow to Settle"
                          : "Startup Apps Will Slow Post-Login Settling",
            Detail:   $"Based on your current startup configuration ({total} entries — {contributorNote}), " +
                      $"your system is estimated to take {timeDesc} to fully settle after the next Windows login. " +
                      $"During this time CPU and disk activity will be elevated while startup apps initialise. " +
                      (growthNote.Length > 0 ? growthNote + " " : "") +
                      (isWarning
                          ? "Reducing the number of high-impact startup applications is the most effective way to improve login responsiveness."
                          : "Disabling startup apps you don't need immediately can significantly reduce this delay."),
            ActionHint:        $"Disable startup apps you don't need at login to cut settling time from {timeDesc}.",
            DetectedAt:        DateTimeOffset.Now,
            ConfidencePercent: 72,   // model is coarse; intentionally modest
            RecommendedActions: s_actions);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildContributorNote(int high, int moderate, int total)
    {
        int other = Math.Max(0, total - high - moderate);
        var parts = new List<string>(3);

        if (high > 0)     parts.Add($"{high} high-impact");
        if (moderate > 0) parts.Add($"{moderate} moderate-impact");
        if (other > 0)    parts.Add($"{other} low-impact");

        return parts.Count switch
        {
            0 => "no high-impact entries",
            1 => parts[0],
            2 => $"{parts[0]} and {parts[1]}",
            _ => $"{parts[0]}, {parts[1]}, and {parts[2]}",
        };
    }

    /// <summary>
    /// Compares current startup entry count against the baseline snapshot.
    /// Returns a sentence noting growth when the count has increased noticeably,
    /// or an empty string when the baseline is not yet ready or growth is negligible.
    /// </summary>
    private string BuildGrowthNote(int currentTotal)
    {
        if (_baseline?.CurrentBaseline is not { } bl)
            return string.Empty;

        int baselineCount = bl.StartupEntryCount;
        if (baselineCount <= 0)
            return string.Empty;

        int growth = currentTotal - baselineCount;
        if (growth < BaselineGrowthGate)
            return string.Empty;

        return $"Your startup list has grown by {growth} entries since this machine was last measured.";
    }
}
