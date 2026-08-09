using Lucid.Services.History;
using Lucid.Services.Replay;

namespace Lucid.Services.Learning;

/// <summary>
/// Analyzes a single <see cref="OperationRecord"/> by reconstructing the system
/// state before and after the action and classifying the net outcome.
///
/// Delegates to:
///   <see cref="IOperationalReplayService.BuildRemediationComparison"/>  — before/after data
///   <see cref="StabilizationClassifier.Classify"/>                     — outcome classification
///
/// Returns null when the action is too recent (not enough post-action data)
/// or when the operation record is not eligible for analysis (dry-run, rollback, failure).
/// </summary>
internal sealed class EffectivenessAnalyzer
{
    // Actions must be at least this old before analysis so post-action telemetry exists.
    private static readonly TimeSpan MinAgeForAnalysis = TimeSpan.FromMinutes(5);

    private readonly IOperationalReplayService _replay;

    public EffectivenessAnalyzer(IOperationalReplayService replay)
    {
        _replay = replay;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Analyzes the given operation and returns an <see cref="ActionOutcomeRecord"/>,
    /// or null when the operation is ineligible or too recent to analyze.
    ///
    /// Always returns a record (possibly with InsufficientData outcome) for
    /// eligible operations so the caller can mark them as processed.
    /// </summary>
    public async Task<ActionOutcomeRecord?> AnalyzeAsync(OperationRecord op)
    {
        // Only analyze real, successful, non-dry-run, non-rollback executions
        if (!op.IsSuccess || op.IsDryRun || op.IsRollback)
            return null;

        // Skip actions that are too recent — not enough post-action data yet
        var age = DateTimeOffset.Now - op.ExecutedAt;
        if (age < MinAgeForAnalysis)
            return null;

        // Build a replay session covering the full available window
        var session    = await _replay.CreateSessionAsync(ReplayWindow.FullSession)
                                      .ConfigureAwait(false);

        // Delegate the before/after comparison to the replay service
        var comparison = _replay.BuildRemediationComparison(session, op);

        // Classify outcome (returns InsufficientData if comparison is null)
        var (outcome, confidence) = StabilizationClassifier.Classify(comparison);

        return BuildRecord(op, comparison, outcome, confidence);
    }

    // ── Record construction ────────────────────────────────────────────────────

    private static ActionOutcomeRecord BuildRecord(
        OperationRecord      op,
        RemediationComparison? comparison,
        OutcomeClassification  outcome,
        int                    confidence)
    {
        double cpuBefore  = 0, cpuAfter  = 0;
        double ramBefore  = 0, ramAfter  = 0;
        double diskBefore = 0, diskAfter = 0;
        int    insightsBefore = 0, insightsAfter = 0, insightsResolved = 0;
        int    stabilizationMinutes = 0;

        if (comparison is not null)
        {
            cpuBefore  = comparison.SnapshotBefore.Telemetry.CpuPercent;
            cpuAfter   = comparison.SnapshotAfter.Telemetry.CpuPercent;
            ramBefore  = comparison.SnapshotBefore.Telemetry.RamPercent;
            ramAfter   = comparison.SnapshotAfter.Telemetry.RamPercent;
            diskBefore = comparison.SnapshotBefore.Telemetry.DiskPercent;
            diskAfter  = comparison.SnapshotAfter.Telemetry.DiskPercent;

            insightsBefore   = comparison.SnapshotBefore.ActiveInsights.Count;
            insightsAfter    = comparison.SnapshotAfter.ActiveInsights.Count;
            insightsResolved = comparison.Delta.ResolvedInsights.Count;

            // Approximate stabilization: time between action and snapshot-after
            // when there is improvement.
            if (comparison.ShowsImprovement)
            {
                var duration = comparison.SnapshotAfter.Timestamp - op.ExecutedAt;
                stabilizationMinutes = (int)Math.Max(0, duration.TotalMinutes);
            }
        }

        return new ActionOutcomeRecord
        {
            OperationId          = op.Id,
            ActionKey            = op.ActionId,
            ActionTitle          = op.ActionTitle,
            ExecutedAt           = op.ExecutedAt,
            AnalyzedAt           = DateTimeOffset.Now,
            Outcome              = outcome,
            ConfidencePercent    = confidence,
            CpuBefore            = cpuBefore,
            CpuAfter             = cpuAfter,
            RamBefore            = ramBefore,
            RamAfter             = ramAfter,
            DiskBefore           = diskBefore,
            DiskAfter            = diskAfter,
            InsightsActiveBefore = insightsBefore,
            InsightsActiveAfter  = insightsAfter,
            InsightsResolved     = insightsResolved,
            StabilizationMinutes = stabilizationMinutes,
            NarrativeSummary     = BuildNarrative(op, comparison, outcome),
        };
    }

    // ── Narrative builder ──────────────────────────────────────────────────────

    private static string BuildNarrative(
        OperationRecord       op,
        RemediationComparison? comparison,
        OutcomeClassification  outcome)
    {
        if (comparison is null)
            return "Insufficient telemetry data around the action time.";

        var delta = comparison.Delta;

        return outcome switch
        {
            OutcomeClassification.Improved =>
                BuildImprovementNarrative(delta),

            OutcomeClassification.PartiallyImproved =>
                BuildPartialNarrative(delta),

            OutcomeClassification.Unchanged =>
                "No significant change detected in the 5-minute window.",

            OutcomeClassification.Worsened =>
                BuildWorseningNarrative(delta),

            _ => "Outcome could not be determined.",
        };
    }

    private static string BuildImprovementNarrative(OperationalDelta delta)
    {
        var parts = new List<string>();

        // Dominant improving metric
        double bestDrop = 0;
        string bestMetric = "";
        double cpuDrop  = -delta.CpuChange;
        double ramDrop  = -delta.RamChange;
        double diskDrop = -delta.DiskChange;

        if (cpuDrop  > bestDrop) { bestDrop = cpuDrop;  bestMetric = "CPU";  }
        if (ramDrop  > bestDrop) { bestDrop = ramDrop;  bestMetric = "RAM";  }
        if (diskDrop > bestDrop) { bestDrop = diskDrop; bestMetric = "disk"; }

        if (bestDrop >= 5 && bestMetric.Length > 0)
            parts.Add($"{bestMetric} pressure dropped ~{bestDrop:F0}%");

        if (delta.ResolvedInsights.Count > 0)
        {
            var count = delta.ResolvedInsights.Count;
            parts.Add($"{count} active {(count == 1 ? "issue" : "issues")} resolved");
        }

        return parts.Count > 0
            ? string.Join(" and ", parts) + "."
            : "System conditions improved following this action.";
    }

    private static string BuildPartialNarrative(OperationalDelta delta)
    {
        if (delta.ResolvedInsights.Count > 0)
            return "A partial improvement was observed — some issues resolved but metrics remained elevated.";

        return "A minor improvement was detected but below the significant threshold.";
    }

    private static string BuildWorseningNarrative(OperationalDelta delta)
    {
        // Note: correlation only — do not imply causation
        double cpuUp  = delta.CpuChange;
        double ramUp  = delta.RamChange;
        double diskUp = delta.DiskChange;

        double maxUp = Math.Max(cpuUp, Math.Max(ramUp, diskUp));
        if (maxUp >= 5)
            return $"System load increased ~{maxUp:F0}% in the window following this action (correlation only).";

        return "Conditions appeared to worsen slightly in the window following this action.";
    }
}
