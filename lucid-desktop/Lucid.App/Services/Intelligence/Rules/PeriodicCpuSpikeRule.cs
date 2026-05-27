using Lucid.Helpers;
using Lucid.Services.Telemetry;

namespace Lucid.Services.Intelligence.Rules;

/// <summary>
/// Fires when the CPU exhibits a recurring pattern of spikes rather than sustained
/// high load or a monotonic escalation.
///
/// This condition is different from <see cref="SustainedHighCpuRule"/>:
///   • Sustained high: CPU is continuously above 80 % — one application is dominating.
///   • Periodic spikes: CPU peaks repeatedly but recovers between spikes — the most
///     common causes are scheduled tasks (Windows Update, antivirus scans, indexing),
///     a polling application, or a recurring background operation.
///
/// Detection method:
///   1. Collect the full 30-minute sample window.
///   2. Count distinct spike episodes: a "spike" is a run of samples above
///      <c>SpikeThreshold</c> (70 %). A new episode starts only after a gap of
///      ≥ 45 s below the threshold (prevents one long spike from counting as many).
///   3. Require the episodes to be distributed across the window (not all at one end)
///      — confirms a recurring pattern, not a one-time burst.
///
/// Minimum requirements:
///   ≥ 3 spike episodes, spread across at least 2 of 3 window thirds,
///   with at least 200 samples in the window (≈ 5 minutes of data).
///
/// Confidence:
///   Scales with episode count and window coverage.
/// </summary>
public sealed class PeriodicCpuSpikeRule : IInsightRule
{
    // CPU% at which a sample is considered part of a spike
    private const double SpikeThreshold       = 70.0;
    // Seconds of recovery needed to end one spike episode and start a new one
    private const double EpisodeCooldownSecs  = 45.0;
    // Minimum distinct episodes before this insight fires
    private const int    MinEpisodeCount      = 3;
    // Minimum 30-min samples to detect a pattern (≈ 5 minutes)
    private const int    MinSamples           = 200;

    private static readonly IReadOnlyList<SystemAction> s_actions =
    [
        new SystemAction(
            Id:                   "action.cpu.open-task-manager",
            Title:                "Open Task Manager",
            Description:          "Watch the CPU column over 30–60 seconds to catch which process spikes and then subsides. The Details tab shows processes that run and exit quickly.",
            Impact:               ActionImpact.Moderate,
            Effort:               ActionEffort.Seconds,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),

        new SystemAction(
            Id:                   "action.cpu.open-task-scheduler",
            Title:                "Check Task Scheduler",
            Description:          "Windows Task Scheduler runs programs on a timed basis. Open it (search 'Task Scheduler') to review recently triggered tasks that may align with the CPU spikes.",
            Impact:               ActionImpact.Moderate,
            Effort:               ActionEffort.FewMinutes,
            Risk:                 ActionRisk.Safe,
            RequiresConfirmation: false,
            IsReversible:         true),

        new SystemAction(
            Id:                   "action.startup.open-startup-apps",
            Title:                "Review Startup Apps",
            Description:          "Some startup applications run periodic maintenance tasks after boot. Disabling unnecessary startup apps may eliminate recurring background spikes.",
            Impact:               ActionImpact.Moderate,
            Effort:               ActionEffort.FewMinutes,
            Risk:                 ActionRisk.Low,
            RequiresConfirmation: false,
            IsReversible:         true),
    ];

    public string RuleId => "cpu.periodic-spikes";

    public SystemInsight? Evaluate(TelemetrySnapshot current, ITelemetryHistoryBuffer history)
    {
        // Require enough history to detect a meaningful pattern
        var stats30m = history.GetStats(TelemetryMetric.Cpu, TimeWindow.LastThirtyMinutes);
        if (stats30m.IsEmpty || stats30m.SampleCount < MinSamples) return null;

        // The long-window average must be below the spike threshold — if CPU is
        // continuously high this is SustainedHighCpuRule territory, not periodic spikes.
        if (stats30m.Average >= SpikeThreshold) return null;

        var samples      = history.GetSamples(TelemetryMetric.Cpu, TimeWindow.LastThirtyMinutes);
        int episodeCount = TrendAnalysis.CountSpikeEpisodes(samples, SpikeThreshold, EpisodeCooldownSecs);

        if (episodeCount < MinEpisodeCount) return null;

        // Confirm the spikes recur across the window, not just at one moment
        if (!TrendAnalysis.SpikesAreDistributed(samples, SpikeThreshold)) return null;

        // Confidence: function of episode count and window coverage
        int coverageConfidence = TrendAnalysis.ConfidenceFromCoverage(
            stats30m.SampleCount, TimeWindow.LastThirtyMinutes, min: 60, max: 88);
        int episodeBonus       = Math.Min(episodeCount - MinEpisodeCount, 4) * 2;
        int confidence         = Math.Min(coverageConfidence + episodeBonus, 92);

        string elapsed  = TrendAnalysis.FormatElapsed(stats30m.SampleCount);
        string episodes = episodeCount == 1 ? "1 spike episode" : $"{episodeCount} spike episodes";

        return new SystemInsight(
            Id:       RuleId,
            Severity: InsightSeverity.Recommendation,
            Title:    "Recurring CPU Spikes Detected",
            Detail:   $"Your processor has experienced {episodes} exceeding {SpikeThreshold:0}% " +
                      $"over the last {elapsed}, with periods of normal usage between them " +
                      $"(average load: {stats30m.Average:0}%). Recurring spikes are most commonly " +
                      $"caused by a scheduled task — such as Windows Update, antivirus scanning, " +
                      $"or a polling application — rather than an actively running application.",
            ActionHint:        "Watch Task Manager during the next spike to identify which process is responsible.",
            DetectedAt:        DateTimeOffset.Now,
            ConfidencePercent: confidence,
            RecommendedActions: s_actions);
    }
}
