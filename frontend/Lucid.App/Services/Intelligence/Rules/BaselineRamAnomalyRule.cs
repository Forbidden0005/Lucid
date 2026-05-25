using Lucid.Helpers;
using Lucid.Services.Baseline;
using Lucid.Services.Telemetry;

namespace Lucid.Services.Intelligence.Rules;

/// <summary>
/// Fires when RAM usage is anomalously high relative to THIS machine's
/// learned normal memory footprint.
///
/// RAM usage varies more than CPU idle levels — a machine with Chrome,
/// an IDE, and several Electron apps open has a higher "normal" than
/// a machine running only a text editor. This rule learns that context
/// and flags genuinely unusual memory consumption for this specific
/// workload and hardware configuration.
///
/// Thresholds (slightly higher than CPU because RAM varies naturally):
///   Minimum absolute delta  ≥ 10 % above normal mean
///   Z-score ≥ 2.5 → Recommendation
///   Z-score ≥ 3.5 → Warning
///
/// Warm-up guard:
///   Returns null until <see cref="ISystemBaselineService.CurrentBaseline"/>
///   becomes non-null (≥ 200 baseline observations ≈ 5 min of learning).
///
/// ID: baseline.ram-above-norm
/// </summary>
public sealed class BaselineRamAnomalyRule : IInsightRule
{
    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.ram.close-browser-tabs",
            Title:                "Close Browser Tabs",
            Description:          "Each browser tab holds memory. Closing unused tabs is the fastest way to bring RAM usage back toward your normal level.",
            Impact:               ActionImpact.Moderate,
            Effort:               ActionEffort.Seconds,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),

        new SystemAction(
            Id:                   "action.ram.restart-heavy-apps",
            Title:                "Restart Heavy Apps",
            Description:          "Applications like browsers and IDEs can accumulate memory over time. Restarting the heaviest apps often returns RAM usage to your baseline level.",
            Impact:               ActionImpact.High,
            Effort:               ActionEffort.FewMinutes,
            Risk:                 ActionRisk.Low,
            RequiresConfirmation: true,
            IsReversible:         true,
            ConfirmationMessage:  "Restarting an app will close its current windows. Save your work first."),
    ];

    private readonly ISystemBaselineService _baseline;

    public string RuleId => "baseline.ram-above-norm";

    public BaselineRamAnomalyRule(ISystemBaselineService baseline)
    {
        _baseline = baseline;
    }

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        // Guard: wait for the baseline warm-up period.
        var baseline = _baseline.CurrentBaseline;
        if (baseline is null || !baseline.NormalRamReady)
            return null;

        if (current.RamTotalGb <= 0)
            return null;

        double ramPct = current.RamPercent;
        double zscore = baseline.RamZScore(ramPct);

        if (!baseline.IsRamAnomaly(ramPct))
            return null;

        bool    isWarning  = zscore >= MachineBaseline.RamSigmaWarning;
        var     severity   = isWarning ? InsightSeverity.Warning : InsightSeverity.Recommendation;
        int     confidence = MachineBaseline.ConfidenceFromZScore(zscore);

        double normalLow  = Math.Max(0, baseline.NormalRamMean - baseline.NormalRamStdDev);
        double normalHigh = baseline.NormalRamMean + baseline.NormalRamStdDev;

        // Express in both % and GB for clarity.
        double normalUsedGb = baseline.NormalRamMean / 100.0 * current.RamTotalGb;
        double currentUsedGb = current.RamUsedGb;

        string detail = isWarning
            ? $"Your machine is using {currentUsedGb:F1} GB of RAM ({ramPct:0}%), which is {zscore:F1}σ " +
              $"above your learned normal of {baseline.NormalRamMean:0}% " +
              $"(normal range: {normalLow:0}–{normalHigh:0}%, about {normalUsedGb:F1} GB). " +
              $"Something is holding significantly more memory than this machine typically does."
            : $"Your RAM usage ({currentUsedGb:F1} GB, {ramPct:0}%) is above your machine's normal level " +
              $"of {baseline.NormalRamMean:0}% (about {normalUsedGb:F1} GB). " +
              $"This may be normal if you've opened new applications, or it could indicate a memory leak.";

        var attributions = ProcessAttributionHelper.ForRam(current.TopProcesses, current.RamTotalGb);

        return new SystemInsight(
            Id:       RuleId,
            Severity: severity,
            Title:    isWarning
                          ? "RAM Significantly Above Your Normal"
                          : "Memory Usage Above Your Machine's Baseline",
            Detail:            detail,
            ActionHint:        "Check Task Manager to see which application is consuming more memory than usual.",
            DetectedAt:        DateTimeOffset.Now,
            ConfidencePercent: confidence,
            RecommendedActions:  s_actions,
            AttributedProcesses: attributions);
    }
}
