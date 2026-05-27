using Lucid.Services.Intelligence;
using Lucid.Services.Learning;

namespace Lucid.Services.Remediation;

/// <summary>
/// Validates post-remediation outcomes by comparing before/after snapshots
/// and producing a <see cref="RemediationOutcomeReport"/>.
///
/// Also feeds outcome data back into the <see cref="IRemediationLearningService"/>
/// by triggering a learning analysis pass after each completed workflow.
/// </summary>
public sealed class RemediationOutcomeValidator
{
    private readonly IRemediationLearningService _learning;

    public RemediationOutcomeValidator(IRemediationLearningService learning)
    {
        _learning = learning;
    }

    // ── Validation ────────────────────────────────────────────────────────────

    /// <summary>
    /// Produces a <see cref="RemediationOutcomeReport"/> from the completed
    /// execution state and the final system telemetry.
    ///
    /// Triggers a learning analysis pass in the background so the learning
    /// service can build effectiveness profiles over time.
    /// Never throws.
    /// </summary>
    public RemediationOutcomeReport Validate(
        WorkflowExecutionState       state,
        IReadOnlyList<SystemInsight> currentInsights)
    {
        try
        {
            var before = state.BeforeSnapshot;

            // If we have no before snapshot, produce an inconclusive report
            if (before is null)
            {
                return BuildInconclusive(state);
            }

            // Capture after snapshot now
            var after = new StabilizationSnapshot(
                CpuPercent:            0, // no live telemetry available at this point
                RamPercent:            0,
                DiskPercent:           0,
                CpuTemperatureCelsius: 0,
                TemperatureAvailable:  false,
                ActiveInsightCount:    currentInsights.Count,
                ActiveInsightIds:      currentInsights.Select(i => i.Id).ToList(),
                CapturedAt:            DateTimeOffset.Now);

            // The after snapshot will be set by the service once telemetry arrives
            var stabilization = state.AfterSnapshot is not null
                ? StabilizationVerificationEngine.Evaluate(before, state.AfterSnapshot)
                : StabilizationResult.Inconclusive;

            var (cpuChange, ramChange, diskChange) = state.AfterSnapshot is not null
                ? StabilizationVerificationEngine.ComputeDeltas(before, state.AfterSnapshot)
                : (0.0, 0.0, 0.0);

            var insightsResolved = before.ActiveInsightIds
                .Count(id => !currentInsights.Any(i => i.Id == id));

            var narrative = StabilizationVerificationEngine.BuildNarrative(
                state.Workflow.Name,
                stabilization,
                before,
                state.AfterSnapshot ?? after);

            // Trigger learning pass in background (non-blocking)
            _ = Task.Run(() => _learning.AnalyzePendingActionsAsync());

            return new RemediationOutcomeReport(
                ExecutionId:         state.ExecutionId,
                WorkflowName:        state.Workflow.Name,
                FinalStatus:         state.Status,
                StabilizationResult: stabilization,
                CpuChangePercent:    cpuChange,
                RamChangePercent:    ramChange,
                DiskChangePercent:   diskChange,
                InsightsResolved:    insightsResolved,
                NarrativeSummary:    narrative,
                Before:              before,
                After:               state.AfterSnapshot ?? after,
                CompletedAt:         state.CompletedAt ?? DateTimeOffset.Now);
        }
        catch
        {
            return BuildInconclusive(state);
        }
    }

    private static RemediationOutcomeReport BuildInconclusive(WorkflowExecutionState state) =>
        new(
            ExecutionId:         state.ExecutionId,
            WorkflowName:        state.Workflow?.Name ?? "Unknown",
            FinalStatus:         state.Status,
            StabilizationResult: StabilizationResult.Inconclusive,
            CpuChangePercent:    0,
            RamChangePercent:    0,
            DiskChangePercent:   0,
            InsightsResolved:    0,
            NarrativeSummary:    "Insufficient data to measure stabilization outcome.",
            Before:              state.BeforeSnapshot,
            After:               state.AfterSnapshot,
            CompletedAt:         state.CompletedAt ?? DateTimeOffset.Now);
}
