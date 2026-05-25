using Lucid.Services.Intelligence;

namespace Lucid.Services.Remediation;

/// <summary>
/// Defines the built-in remediation workflow templates and builds
/// <see cref="RemediationPlanSuggestion"/> lists from active system insights.
///
/// All workflows are pre-defined and deterministic — no dynamic generation.
/// Suggestions are always advisory; the user decides whether to execute.
/// </summary>
public static class RemediationWorkflowPlanner
{
    // ── Built-in workflow IDs ─────────────────────────────────────────────────
    public const string StartupRecoveryId      = "workflow.startup-recovery";
    public const string StorageRecoveryId      = "workflow.storage-recovery";
    public const string NetworkRecoveryId      = "workflow.network-recovery";
    public const string ThermalStabilizationId = "workflow.thermal-stabilization";
    public const string DiskMaintenanceId      = "workflow.disk-maintenance";

    // ── Workflow catalog ──────────────────────────────────────────────────────

    /// <summary>
    /// All available workflow templates.
    /// </summary>
    public static IReadOnlyList<RemediationWorkflow> AllWorkflows { get; } =
    [
        BuildStartupRecoveryWorkflow(),
        BuildStorageRecoveryWorkflow(),
        BuildNetworkRecoveryWorkflow(),
        BuildThermalStabilizationWorkflow(),
        BuildDiskMaintenanceWorkflow(),
    ];

    // ── Suggestion logic ──────────────────────────────────────────────────────

    /// <summary>
    /// Produces a ranked list of workflow suggestions from the current insight set.
    /// Returns at most 4 suggestions, ordered by relevance.
    /// </summary>
    public static IReadOnlyList<RemediationPlanSuggestion> BuildSuggestions(
        IReadOnlyList<SystemInsight> insights)
    {
        var suggestions = new List<RemediationPlanSuggestion>();
        var now         = DateTimeOffset.Now;

        // Classify active insight IDs for matching
        var insightIds = insights.Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ── Startup Recovery ──────────────────────────────────────────────────
        var startupInsights = insights.Where(i =>
            i.Id.Contains("startup", StringComparison.OrdinalIgnoreCase) &&
            i.Severity >= InsightSeverity.Recommendation).ToList();

        if (startupInsights.Count > 0)
        {
            suggestions.Add(new RemediationPlanSuggestion(
                Workflow:           AllWorkflows.First(w => w.WorkflowId == StartupRecoveryId),
                Rationale:          $"{startupInsights.Count} startup-related " +
                                    (startupInsights.Count == 1 ? "issue is" : "issues are") +
                                    " active. A startup recovery workflow can back up the current " +
                                    "state and help reduce startup overhead.",
                ConfidencePercent:  75,
                TriggeringInsightIds: startupInsights.Select(i => i.Id).ToList(),
                ExpectedOutcomeText: "Reduced startup overhead and faster boot times",
                SuggestedAt:        now));
        }

        // ── Storage Recovery ──────────────────────────────────────────────────
        var diskInsights = insights.Where(i =>
            (i.Id.Contains("disk", StringComparison.OrdinalIgnoreCase) ||
             i.Id.Contains("storage", StringComparison.OrdinalIgnoreCase)) &&
            i.Severity >= InsightSeverity.Recommendation).ToList();

        if (diskInsights.Count > 0)
        {
            suggestions.Add(new RemediationPlanSuggestion(
                Workflow:           AllWorkflows.First(w => w.WorkflowId == StorageRecoveryId),
                Rationale:          $"Disk pressure is active. A storage recovery workflow can " +
                                    "clear temporary files and caches to free space.",
                ConfidencePercent:  80,
                TriggeringInsightIds: diskInsights.Select(i => i.Id).ToList(),
                ExpectedOutcomeText: "Reclaimed disk space and reduced disk pressure",
                SuggestedAt:        now));
        }

        // ── Network Recovery ─────────────────────────────────────────────────
        var networkInsights = insights.Where(i =>
            i.Id.Contains("network", StringComparison.OrdinalIgnoreCase) ||
            i.Id.Contains("dns", StringComparison.OrdinalIgnoreCase)).ToList();

        if (networkInsights.Count > 0)
        {
            suggestions.Add(new RemediationPlanSuggestion(
                Workflow:           AllWorkflows.First(w => w.WorkflowId == NetworkRecoveryId),
                Rationale:          "Network-related issues are active. Flushing DNS and resetting " +
                                    "Winsock can resolve connectivity problems.",
                ConfidencePercent:  70,
                TriggeringInsightIds: networkInsights.Select(i => i.Id).ToList(),
                ExpectedOutcomeText: "Resolved DNS and network stack issues",
                SuggestedAt:        now));
        }

        // ── Thermal Stabilization ────────────────────────────────────────────
        var thermalInsights = insights.Where(i =>
            (i.Id.Contains("thermal", StringComparison.OrdinalIgnoreCase) ||
             i.Id.Contains("temperature", StringComparison.OrdinalIgnoreCase)) &&
            i.Severity >= InsightSeverity.Warning).ToList();

        if (thermalInsights.Count > 0)
        {
            suggestions.Add(new RemediationPlanSuggestion(
                Workflow:           AllWorkflows.First(w => w.WorkflowId == ThermalStabilizationId),
                Rationale:          "Elevated CPU temperature detected. The thermal workflow can " +
                                    "reduce background activity to help stabilize temperature.",
                ConfidencePercent:  72,
                TriggeringInsightIds: thermalInsights.Select(i => i.Id).ToList(),
                ExpectedOutcomeText: "Reduced CPU temperature within a few minutes",
                SuggestedAt:        now));
        }

        // ── Disk Maintenance (general) — always offered as baseline ───────────
        if (suggestions.None() || suggestions.All(s => s.Workflow.WorkflowId != DiskMaintenanceId))
        {
            suggestions.Add(new RemediationPlanSuggestion(
                Workflow:           AllWorkflows.First(w => w.WorkflowId == DiskMaintenanceId),
                Rationale:          "Routine disk maintenance can free temporary files and help " +
                                    "keep the system running smoothly.",
                ConfidencePercent:  55,
                TriggeringInsightIds: [],
                ExpectedOutcomeText: "Freed temporary files and browser caches",
                SuggestedAt:        now));
        }

        // Sort by confidence descending
        suggestions.Sort((a, b) => b.ConfidencePercent.CompareTo(a.ConfidencePercent));
        return suggestions.Take(4).ToList();
    }

    // ── Workflow builders ─────────────────────────────────────────────────────

    private static RemediationWorkflow BuildStartupRecoveryWorkflow() => new(
        WorkflowId:               StartupRecoveryId,
        Name:                     "Startup Recovery",
        Description:              "Backs up the current startup configuration and helps " +
                                  "identify high-impact startup entries that may be increasing boot time.",
        Category:                 "Startup",
        Steps: [
            new WorkflowStepDefinition(
                StepId:       "startup.backup",
                Name:         "Backup startup state",
                Description:  "Creates a restorable snapshot of all current startup entries.",
                ActionKey:    "action.startup.backup-startup-state",
                Phase:        WorkflowPhase.Backup),

            new WorkflowStepDefinition(
                StepId:       "startup.open-apps",
                Name:         "Open Startup Apps manager",
                Description:  "Opens the Windows Startup Apps settings for review.",
                ActionKey:    "action.startup.open-startup-apps",
                Phase:        WorkflowPhase.Execution,
                IsOptional:   true),
        ],
        TrustLevelRequired:       AutomationTrustLevel.GuidedApproval,
        Risk:                     OperationalRisk.Low,
        IsReversible:             true,
        EstimatedDurationSeconds: 30,
        RequiresRestart:          false,
        ExpectedBenefit:          "Startup state documented; high-impact items visible for review",
        Glyph:                    "");

    private static RemediationWorkflow BuildStorageRecoveryWorkflow() => new(
        WorkflowId:               StorageRecoveryId,
        Name:                     "Storage Recovery",
        Description:              "Clears temporary files, Windows Update cache, and delivery " +
                                  "optimization data to reclaim disk space and reduce disk pressure.",
        Category:                 "Storage",
        Steps: [
            new WorkflowStepDefinition(
                StepId:       "storage.clean-temp",
                Name:         "Clear temporary files",
                Description:  "Removes user and system temp files.",
                ActionKey:    "action.disk.clean-temp-files",
                Phase:        WorkflowPhase.Execution),

            new WorkflowStepDefinition(
                StepId:       "storage.update-cache",
                Name:         "Clear Windows Update cache",
                Description:  "Removes cached Windows Update packages that are no longer needed.",
                ActionKey:    "action.disk.clean-windows-update-cache",
                Phase:        WorkflowPhase.Execution,
                IsOptional:   true),

            new WorkflowStepDefinition(
                StepId:       "storage.delivery-opt",
                Name:         "Clear delivery optimization cache",
                Description:  "Removes peer-to-peer update delivery cache.",
                ActionKey:    "action.disk.clean-delivery-optimization",
                Phase:        WorkflowPhase.Execution,
                IsOptional:   true),
        ],
        TrustLevelRequired:       AutomationTrustLevel.GuidedApproval,
        Risk:                     OperationalRisk.Low,
        IsReversible:             false,
        EstimatedDurationSeconds: 60,
        RequiresRestart:          false,
        ExpectedBenefit:          "Reclaimed disk space from temporary and cached files",
        Glyph:                    "");

    private static RemediationWorkflow BuildNetworkRecoveryWorkflow() => new(
        WorkflowId:               NetworkRecoveryId,
        Name:                     "Network Recovery",
        Description:              "Flushes the DNS resolver cache and resets the Windows " +
                                  "Sockets (Winsock) catalog to resolve common connectivity issues.",
        Category:                 "Network",
        Steps: [
            new WorkflowStepDefinition(
                StepId:       "network.flush-dns",
                Name:         "Flush DNS cache",
                Description:  "Clears the DNS resolver cache to resolve stale entries.",
                ActionKey:    "action.network.flush-dns",
                Phase:        WorkflowPhase.Execution),

            new WorkflowStepDefinition(
                StepId:       "network.winsock-reset",
                Name:         "Reset Winsock catalog",
                Description:  "Resets the Windows Sockets catalog to the default state. " +
                              "A restart is required after this step.",
                ActionKey:    "action.network.winsock-reset",
                Phase:        WorkflowPhase.Execution,
                RequiresElevation:    true,
                RequiresConfirmation: true,
                IsOptional:           true),
        ],
        TrustLevelRequired:       AutomationTrustLevel.GuidedApproval,
        Risk:                     OperationalRisk.Moderate,
        IsReversible:             false,
        EstimatedDurationSeconds: 20,
        RequiresRestart:          true,
        ExpectedBenefit:          "Resolved DNS and Winsock connectivity issues",
        Glyph:                    "");

    private static RemediationWorkflow BuildThermalStabilizationWorkflow() => new(
        WorkflowId:               ThermalStabilizationId,
        Name:                     "Thermal Stabilization",
        Description:              "Opens system tools to help investigate and reduce elevated CPU " +
                                  "temperature. Reviews background processes for workload reduction.",
        Category:                 "Thermal",
        Steps: [
            new WorkflowStepDefinition(
                StepId:       "thermal.open-task-manager",
                Name:         "Open Task Manager",
                Description:  "Opens Task Manager to identify CPU-intensive processes.",
                ActionKey:    "action.cpu.open-task-manager",
                Phase:        WorkflowPhase.Execution),
        ],
        TrustLevelRequired:       AutomationTrustLevel.ManualOnly,
        Risk:                     OperationalRisk.Minimal,
        IsReversible:             true,
        EstimatedDurationSeconds: 10,
        RequiresRestart:          false,
        ExpectedBenefit:          "Visibility into CPU-intensive processes for workload reduction",
        Glyph:                    "");

    private static RemediationWorkflow BuildDiskMaintenanceWorkflow() => new(
        WorkflowId:               DiskMaintenanceId,
        Name:                     "Disk Maintenance",
        Description:              "Routine disk cleanup: clears temp files, empties the recycle bin, " +
                                  "and removes browser caches.",
        Category:                 "Maintenance",
        Steps: [
            new WorkflowStepDefinition(
                StepId:       "disk.clean-temp",
                Name:         "Clear temporary files",
                Description:  "Removes user and system temp files.",
                ActionKey:    "action.disk.clean-temp-files",
                Phase:        WorkflowPhase.Execution),

            new WorkflowStepDefinition(
                StepId:       "disk.recycle-bin",
                Name:         "Empty Recycle Bin",
                Description:  "Permanently removes items in the Recycle Bin.",
                ActionKey:    "action.disk.empty-recycle-bin",
                Phase:        WorkflowPhase.Execution,
                IsOptional:   true),

            new WorkflowStepDefinition(
                StepId:       "disk.browser-cache",
                Name:         "Clear browser caches",
                Description:  "Removes browser-managed cache files.",
                ActionKey:    "action.disk.clean-browser-cache",
                Phase:        WorkflowPhase.Execution,
                IsOptional:   true),
        ],
        TrustLevelRequired:       AutomationTrustLevel.GuidedApproval,
        Risk:                     OperationalRisk.Low,
        IsReversible:             false,
        EstimatedDurationSeconds: 45,
        RequiresRestart:          false,
        ExpectedBenefit:          "Freed temporary files and browser cache space",
        Glyph:                    "");
}

// ── Extension helper ──────────────────────────────────────────────────────────

internal static class EnumerableExtensions
{
    public static bool None<T>(this IEnumerable<T> source) => !source.Any();
}
