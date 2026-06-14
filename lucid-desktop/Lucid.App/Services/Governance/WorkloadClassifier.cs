namespace Lucid.Services.Governance;

/// <summary>
/// Maps a <see cref="WorkloadCategory"/> to its <see cref="WorkloadPriority"/>.
///
/// This is the single authoritative source for the priority of each workload
/// type. It is intentionally static — priority assignments must not vary
/// at runtime based on system state. Policy variation (what's allowed under
/// which <see cref="RuntimeMode"/>) lives in <see cref="AdaptiveSchedulingPolicy"/>.
/// </summary>
internal static class WorkloadClassifier
{
    /// <summary>
    /// Returns the operational priority for the given workload category.
    /// </summary>
    public static WorkloadPriority Classify(WorkloadCategory category) => category switch
    {
        // Foreground — user-initiated, time-sensitive, never deferred
        WorkloadCategory.ActionExecution  => WorkloadPriority.Foreground,
        WorkloadCategory.ExplainReasoning => WorkloadPriority.Foreground,
        WorkloadCategory.ReplayAnalysis   => WorkloadPriority.Foreground,

        // Background — scheduled/passive, paused under stress
        WorkloadCategory.TelemetrySampling   => WorkloadPriority.Background,
        WorkloadCategory.ProcessIntelligence => WorkloadPriority.Background,
        WorkloadCategory.NarrativeGeneration => WorkloadPriority.Background,
        WorkloadCategory.TimelineAggregation => WorkloadPriority.Background,
        WorkloadCategory.InsightAnalysis     => WorkloadPriority.Background,

        // IdleOnly — heavy scans only when the system is calm
        WorkloadCategory.StorageScan         => WorkloadPriority.IdleOnly,
        WorkloadCategory.DuplicateHashing    => WorkloadPriority.IdleOnly,
        WorkloadCategory.HistoricalAnalytics => WorkloadPriority.IdleOnly,
        WorkloadCategory.LearningAnalysis    => WorkloadPriority.IdleOnly,
        WorkloadCategory.RollbackMaintenance => WorkloadPriority.IdleOnly,

        _ => WorkloadPriority.Background,
    };

    /// <summary>
    /// Returns a concise display name for a workload category.
    /// </summary>
    public static string GetDisplayName(WorkloadCategory category) => category switch
    {
        WorkloadCategory.ActionExecution     => "Action Execution",
        WorkloadCategory.ExplainReasoning    => "Explain Analysis",
        WorkloadCategory.ReplayAnalysis      => "Replay Reconstruction",
        WorkloadCategory.TelemetrySampling   => "Telemetry Sampling",
        WorkloadCategory.ProcessIntelligence => "Process Intelligence",
        WorkloadCategory.NarrativeGeneration => "Narrative Generation",
        WorkloadCategory.TimelineAggregation => "Timeline Aggregation",
        WorkloadCategory.InsightAnalysis     => "Insight Analysis",
        WorkloadCategory.StorageScan         => "Storage Scan",
        WorkloadCategory.DuplicateHashing    => "Duplicate Detection",
        WorkloadCategory.HistoricalAnalytics => "Historical Analytics",
        WorkloadCategory.LearningAnalysis    => "Learning Analysis",
        WorkloadCategory.RollbackMaintenance => "Rollback Maintenance",
        _ => category.ToString(),
    };
}
