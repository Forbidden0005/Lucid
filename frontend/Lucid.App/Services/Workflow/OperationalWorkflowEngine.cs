using Lucid.Services.Reasoning;

namespace Lucid.Services.Workflow;

// ── Interface ─────────────────────────────────────────────────────────────────

/// <summary>
/// Builds guided, evidence-driven operational workflows from root cause candidates
/// and intelligence findings.
///
/// Every workflow produced by this engine:
///   • Explains WHY it exists (evidence-grounded problem summary).
///   • Explains WHAT evidence supports it (EvidenceSummary).
///   • Explains WHAT the expected outcome is (ExpectedOutcome).
///   • Explains WHAT to do if it doesn't work (FallbackActions).
///   • Reports its historical success rate from the learning profile.
///
/// Safety contract (non-negotiable):
///   • Workflows are SUGGESTIONS — they are never auto-executed.
///   • Each step carries an ActionId that maps to a registered executor.
///   • Elevated operations require explicit trust-level escalation by the user.
///   • If stabilization worsens after a step, execution must stop.
///   • The rollback-first philosophy is embedded into step design.
/// </summary>
public interface IOperationalWorkflowEngine
{
    /// <summary>
    /// Builds a single workflow from a root cause candidate.
    /// Returns null when the candidate has insufficient evidence to support a workflow.
    /// </summary>
    OperationalWorkflow? BuildWorkflow(
        RootCauseCandidate                                     candidate,
        IReadOnlyDictionary<string, EffectivenessProfile>?     profiles = null);

    /// <summary>
    /// Returns up to <paramref name="maxWorkflows"/> suggested workflows based
    /// on the current evidence graph and root cause candidates.
    ///
    /// Workflows are ordered by priority (Critical first) then by confidence.
    /// </summary>
    IReadOnlyList<OperationalWorkflow> GetSuggestedWorkflows(
        IReadOnlyList<RootCauseCandidate>                      candidates,
        IReadOnlyDictionary<string, EffectivenessProfile>?     profiles = null,
        int                                                     maxWorkflows = 3);
}

// ── Local effectiveness profile record ───────────────────────────────────────

/// <summary>
/// Minimal effectiveness profile used by the workflow engine.
/// Populated from <see cref="Lucid.Services.Learning.RecommendationEffectivenessProfile"/>
/// via AppServices or passed as null for cold-start scenarios.
/// </summary>
public sealed record EffectivenessProfile(double SuccessRate);

// ── Implementation ────────────────────────────────────────────────────────────

/// <summary>
/// Deterministic workflow generation engine.
///
/// Workflow selection algorithm:
///   1. Match each root cause candidate to a domain-specific workflow template.
///   2. Populate template placeholders from candidate evidence.
///   3. Adjust priority based on candidate confidence and evidence chain depth.
///   4. Annotate steps with historical success rates from the learning profile.
///   5. Return ordered list of workflows ready for display.
///
/// Bounded output: each call produces a finite, inspectable result.
/// Never runs any action automatically.
/// </summary>
public sealed class OperationalWorkflowEngine : IOperationalWorkflowEngine
{
    public OperationalWorkflow? BuildWorkflow(
        RootCauseCandidate                                  candidate,
        IReadOnlyDictionary<string, EffectivenessProfile>? profiles = null)
    {
        if (candidate.Confidence < 0.30) return null;

        var template = SelectTemplate(candidate);
        if (template is null) return null;

        return template(candidate, profiles);
    }

    public IReadOnlyList<OperationalWorkflow> GetSuggestedWorkflows(
        IReadOnlyList<RootCauseCandidate>                  candidates,
        IReadOnlyDictionary<string, EffectivenessProfile>? profiles = null,
        int                                                 maxWorkflows = 3)
    {
        var workflows = new List<OperationalWorkflow>();

        foreach (var candidate in candidates)
        {
            var wf = BuildWorkflow(candidate, profiles);
            if (wf is not null) workflows.Add(wf);
            if (workflows.Count >= maxWorkflows) break;
        }

        return workflows
            .OrderBy(w => (int)w.Priority)
            .ThenByDescending(w => w.HistoricalSuccessRate)
            .ToList();
    }

    // ── Template selection ────────────────────────────────────────────────────

    private static Func<RootCauseCandidate, IReadOnlyDictionary<string, EffectivenessProfile>?, OperationalWorkflow>?
        SelectTemplate(RootCauseCandidate candidate)
    {
        var lower  = (candidate.CauseTitle + " " + candidate.Explanation).ToLowerInvariant();
        var areas  = candidate.AffectedSystems.Select(a => a.ToLowerInvariant()).ToHashSet();

        if (areas.Contains("ram") || lower.Contains("ram") || lower.Contains("memory"))
            return BuildRamPressureWorkflow;

        if (areas.Contains("disk") || lower.Contains("disk") || lower.Contains("storage"))
            return BuildDiskPressureWorkflow;

        if (areas.Contains("cpu") || lower.Contains("cpu") || lower.Contains("processor"))
            return BuildHighCpuWorkflow;

        if (areas.Contains("thermal") || lower.Contains("thermal") || lower.Contains("temperature"))
            return BuildThermalWorkflow;

        if (areas.Contains("startup") || lower.Contains("startup") || lower.Contains("boot"))
            return BuildStartupWorkflow;

        if (lower.Contains("anomaly") || lower.Contains("drift"))
            return BuildGenericAnomalyWorkflow;

        return BuildGenericWorkflow;
    }

    // ── Workflow templates ────────────────────────────────────────────────────

    private static OperationalWorkflow BuildRamPressureWorkflow(
        RootCauseCandidate                                  candidate,
        IReadOnlyDictionary<string, EffectivenessProfile>? profile)
    {
        var priority = candidate.Confidence >= 0.75
            ? WorkflowPriority.High
            : WorkflowPriority.Medium;

        return new OperationalWorkflow
        {
            Id          = NewId(),
            Title       = "High RAM Pressure Investigation",
            ProblemSummary  = candidate.Explanation.Length > 0
                ? candidate.Explanation
                : "System RAM usage is elevated, which may be causing performance degradation.",
            EvidenceSummary = BuildEvidenceSummary(candidate),
            RiskSummary     = "Sustained high RAM usage can lead to excessive paging (disk thrashing), " +
                              "application slowdowns, or crashes if physical memory is fully exhausted.",
            Steps           = [
                new WorkflowStep
                {
                    StepNumber       = 1,
                    StepTitle        = "Open Task Manager",
                    StepDescription  = "Identify which processes are consuming the most memory. " +
                                       "Look for any unexpected applications consuming unusually large amounts of RAM.",
                    ActionId         = "action.cpu.open-task-manager",
                    ExpectedEffect   = "Task Manager shows the top RAM consumers ranked by usage.",
                    IsOptional       = false,
                },
                new WorkflowStep
                {
                    StepNumber       = 2,
                    StepTitle        = "Observe RAM usage for 2–3 minutes",
                    StepDescription  = "Watch whether RAM usage is growing steadily or is stable at its current level. " +
                                       "Growing usage suggests a memory leak; stable usage means the system is under load.",
                    ActionId         = string.Empty,
                    ExpectedEffect   = "You should be able to tell whether usage is growing or stable.",
                    IsOptional       = false,
                },
                new WorkflowStep
                {
                    StepNumber       = 3,
                    StepTitle        = "Clear temporary files",
                    StepDescription  = "Removing cached temporary files frees disk space and can reduce the working set " +
                                       "of some applications, indirectly reducing memory pressure.",
                    ActionId         = "action.disk.clean-temp-files",
                    ExpectedEffect   = "RAM usage should drop 5–10% if temporary file caches were inflating working sets.",
                    IsOptional       = true,
                },
            ],
            ExpectedOutcome  = "RAM usage should stabilise below the warning threshold within 5–10 minutes " +
                               "after the highest-consuming process is identified and addressed.",
            FallbackActions  = [
                "Restart the highest-consuming process identified in Task Manager.",
                "Consider closing unused browser tabs if a browser is the primary consumer.",
                "Reboot the system if RAM usage remains critically high after other steps.",
            ],
            HistoricalSuccessRate = GetSuccessRate(profile, "action.disk.clean-temp-files"),
            Priority              = priority,
            CreatedAt             = DateTimeOffset.Now,
        };
    }

    private static OperationalWorkflow BuildDiskPressureWorkflow(
        RootCauseCandidate                                  candidate,
        IReadOnlyDictionary<string, EffectivenessProfile>? profile)
    {
        return new OperationalWorkflow
        {
            Id          = NewId(),
            Title       = "Disk Space & I/O Pressure Investigation",
            ProblemSummary  = candidate.Explanation.Length > 0
                ? candidate.Explanation
                : "Disk space or I/O activity is outside normal operating parameters.",
            EvidenceSummary = BuildEvidenceSummary(candidate),
            RiskSummary     = "Low disk space can prevent Windows from creating page files, cause installer failures, " +
                              "and degrade overall system responsiveness. High I/O may indicate a runaway process.",
            Steps           = [
                new WorkflowStep
                {
                    StepNumber       = 1,
                    StepTitle        = "Open Storage Sense",
                    StepDescription  = "Review disk usage by category to identify the largest contributors to disk consumption.",
                    ActionId         = "action.disk.open-storage-sense",
                    ExpectedEffect   = "Storage Sense shows usage breakdown by system files, apps, and user data.",
                    IsOptional       = false,
                },
                new WorkflowStep
                {
                    StepNumber       = 2,
                    StepTitle        = "Clean temporary files",
                    StepDescription  = "Temporary files accumulate from Windows Update, application caches, and browser activity. " +
                                       "Cleaning them is safe and typically recovers 1–5 GB.",
                    ActionId         = "action.disk.clean-temp-files",
                    ExpectedEffect   = "1–5 GB of disk space should be reclaimed from temporary file caches.",
                    IsOptional       = false,
                },
                new WorkflowStep
                {
                    StepNumber       = 3,
                    StepTitle        = "Empty the Recycle Bin",
                    StepDescription  = "Files in the Recycle Bin still consume disk space. Emptying it reclaims space immediately.",
                    ActionId         = "action.disk.empty-recycle-bin",
                    ExpectedEffect   = "Disk space is recovered proportional to Recycle Bin contents.",
                    IsOptional       = true,
                },
                new WorkflowStep
                {
                    StepNumber       = 4,
                    StepTitle        = "Clear Windows Update cache",
                    StepDescription  = "The Windows Update cache can accumulate gigabytes of downloaded installer files. " +
                                       "Clearing it is safe — Windows will re-download updates only if needed.",
                    ActionId         = "action.disk.clean-windows-update-cache",
                    ExpectedEffect   = "Up to several GB may be reclaimed from the SoftwareDistribution cache.",
                    IsOptional       = true,
                },
            ],
            ExpectedOutcome  = "Disk usage should drop measurably after cleanup steps complete. " +
                               "If I/O pressure was the primary issue, monitor for the responsible process in Task Manager.",
            FallbackActions  = [
                "Run Storage Sense on a full scan to identify large files in user directories.",
                "Use the Storage Intelligence page to find duplicate or large files.",
                "Check whether a Windows Update is downloading in the background.",
            ],
            HistoricalSuccessRate = GetSuccessRate(profile, "action.disk.clean-temp-files"),
            Priority              = candidate.Confidence >= 0.75 ? WorkflowPriority.High : WorkflowPriority.Medium,
            CreatedAt             = DateTimeOffset.Now,
        };
    }

    private static OperationalWorkflow BuildHighCpuWorkflow(
        RootCauseCandidate                                  candidate,
        IReadOnlyDictionary<string, EffectivenessProfile>? profile)
    {
        return new OperationalWorkflow
        {
            Id          = NewId(),
            Title       = "Sustained High CPU Investigation",
            ProblemSummary  = candidate.Explanation.Length > 0
                ? candidate.Explanation
                : "CPU usage is elevated above normal operating range for this machine.",
            EvidenceSummary = BuildEvidenceSummary(candidate),
            RiskSummary     = "Sustained high CPU usage degrades system responsiveness and may cause thermal throttling, " +
                              "which further reduces performance and increases wear on cooling components.",
            Steps           = [
                new WorkflowStep
                {
                    StepNumber       = 1,
                    StepTitle        = "Open Task Manager",
                    StepDescription  = "Identify the process consuming the most CPU. Sort by CPU % to find the culprit.",
                    ActionId         = "action.cpu.open-task-manager",
                    ExpectedEffect   = "Task Manager shows which process is driving CPU usage.",
                    IsOptional       = false,
                },
                new WorkflowStep
                {
                    StepNumber       = 2,
                    StepTitle        = "Observe for 2 minutes",
                    StepDescription  = "Determine whether the high CPU usage is sustained or transient. " +
                                       "Some Windows background tasks (indexing, antivirus scans) produce short spikes " +
                                       "that resolve on their own.",
                    ActionId         = string.Empty,
                    ExpectedEffect   = "CPU should drop if the cause is a transient background task.",
                    IsOptional       = false,
                },
                new WorkflowStep
                {
                    StepNumber       = 3,
                    StepTitle        = "Flush DNS cache",
                    StepDescription  = "If a networking-related process is driving CPU, flushing the DNS cache can " +
                                       "resolve lookup storms caused by stale or corrupted DNS entries.",
                    ActionId         = "action.network.flush-dns",
                    ExpectedEffect   = "CPU reduction if a networking process was the root cause.",
                    IsOptional       = true,
                },
            ],
            ExpectedOutcome  = "CPU usage should return to this machine's baseline within 3–5 minutes " +
                               "after the responsible process is identified and addressed.",
            FallbackActions  = [
                "Restart the high-CPU process if it is not a critical system process.",
                "Check Windows Update status — updates often trigger CPU-intensive background work.",
                "Reboot if CPU usage remains high and no single process is clearly responsible.",
            ],
            HistoricalSuccessRate = GetSuccessRate(profile, "action.cpu.open-task-manager"),
            Priority              = candidate.Confidence >= 0.80 ? WorkflowPriority.High : WorkflowPriority.Medium,
            CreatedAt             = DateTimeOffset.Now,
        };
    }

    private static OperationalWorkflow BuildThermalWorkflow(
        RootCauseCandidate                                  candidate,
        IReadOnlyDictionary<string, EffectivenessProfile>? profile)
    {
        var priority = candidate.Confidence >= 0.75
            ? WorkflowPriority.Critical
            : WorkflowPriority.High;

        return new OperationalWorkflow
        {
            Id          = NewId(),
            Title       = "Elevated Thermal Condition Investigation",
            ProblemSummary  = candidate.Explanation.Length > 0
                ? candidate.Explanation
                : "System temperature is elevated above normal operating range.",
            EvidenceSummary = BuildEvidenceSummary(candidate),
            RiskSummary     = "Sustained elevated temperatures can cause thermal throttling (performance degradation), " +
                              "accelerated hardware wear, and in severe cases unexpected shutdowns.",
            Steps           = [
                new WorkflowStep
                {
                    StepNumber       = 1,
                    StepTitle        = "Check physical cooling",
                    StepDescription  = "Ensure the device has adequate ventilation. Confirm that vents are not blocked " +
                                       "and the fan is audible during load. Laptops should not be used on soft surfaces.",
                    ActionId         = string.Empty,
                    ExpectedEffect   = "Improved airflow should cause temperatures to decrease within 2–3 minutes.",
                    IsOptional       = false,
                },
                new WorkflowStep
                {
                    StepNumber       = 2,
                    StepTitle        = "Identify high-CPU processes",
                    StepDescription  = "High CPU usage is the most common cause of elevated temperatures. " +
                                       "Open Task Manager to find and address any unexpectedly high-CPU processes.",
                    ActionId         = "action.cpu.open-task-manager",
                    ExpectedEffect   = "Reducing CPU load should bring temperatures down within 1–3 minutes.",
                    IsOptional       = false,
                },
                new WorkflowStep
                {
                    StepNumber       = 3,
                    StepTitle        = "Observe temperature trend",
                    StepDescription  = "Monitor the thermal readings on the Dashboard for 3–5 minutes. " +
                                       "Temperatures should trend downward if physical causes were addressed.",
                    ActionId         = string.Empty,
                    ExpectedEffect   = "Temperature should stabilise and decline once CPU load is reduced.",
                    IsOptional       = false,
                },
            ],
            ExpectedOutcome  = "System temperature should return to normal operating range within 5–10 minutes " +
                               "after CPU load is reduced and ventilation is confirmed adequate.",
            FallbackActions  = [
                "Consider restarting the system to clear any thermal runaway condition.",
                "Schedule a dust cleaning if temperatures remain elevated after CPU load is reduced.",
                "If throttling continues, check Windows power plan — high-performance mode can prevent throttling.",
            ],
            HistoricalSuccessRate = GetSuccessRate(profile, "action.cpu.open-task-manager"),
            Priority              = priority,
            CreatedAt             = DateTimeOffset.Now,
        };
    }

    private static OperationalWorkflow BuildStartupWorkflow(
        RootCauseCandidate                                  candidate,
        IReadOnlyDictionary<string, EffectivenessProfile>? profile)
    {
        return new OperationalWorkflow
        {
            Id          = NewId(),
            Title       = "Startup Performance Optimisation",
            ProblemSummary  = candidate.Explanation.Length > 0
                ? candidate.Explanation
                : "A large number of startup programs are affecting boot time and initial resource usage.",
            EvidenceSummary = BuildEvidenceSummary(candidate),
            RiskSummary     = "Excessive startup programs extend boot time and consume CPU, RAM, and disk I/O " +
                              "during the critical first few minutes after login.",
            Steps           = [
                new WorkflowStep
                {
                    StepNumber       = 1,
                    StepTitle        = "Review startup programs",
                    StepDescription  = "Open the Startup Apps management view to see all programs that launch at login. " +
                                       "Review which are necessary for your workflow.",
                    ActionId         = "action.startup.open-startup-apps",
                    ExpectedEffect   = "A list of all startup programs is shown with their impact levels.",
                    IsOptional       = false,
                },
                new WorkflowStep
                {
                    StepNumber       = 2,
                    StepTitle        = "Identify high-impact programs",
                    StepDescription  = "Focus on programs rated 'High' impact that you don't actively use at login. " +
                                       "Applications like cloud sync clients and update managers often run at startup " +
                                       "but can be launched manually when needed.",
                    ActionId         = string.Empty,
                    ExpectedEffect   = "You can identify 1–3 programs safe to defer from automatic startup.",
                    IsOptional       = false,
                },
                new WorkflowStep
                {
                    StepNumber       = 3,
                    StepTitle        = "Backup current startup state",
                    StepDescription  = "Before disabling any startup programs, create a backup snapshot. " +
                                       "This allows you to restore all startup items if you change your mind.",
                    ActionId         = "action.startup.backup-startup-state",
                    ExpectedEffect   = "A backup snapshot is saved — you can restore it at any time.",
                    IsOptional       = false,
                },
            ],
            ExpectedOutcome  = "Boot time should improve after the next restart. " +
                               "RAM usage in the first 5 minutes after login should be measurably lower.",
            FallbackActions  = [
                "If a disabled app causes problems, restore the startup state backup from the Repairs page.",
                "Consider scheduling the app to launch manually from a shortcut when you actually need it.",
            ],
            HistoricalSuccessRate = GetSuccessRate(profile, "action.startup.open-startup-apps"),
            Priority              = WorkflowPriority.Medium,
            CreatedAt             = DateTimeOffset.Now,
        };
    }

    private static OperationalWorkflow BuildGenericAnomalyWorkflow(
        RootCauseCandidate                                  candidate,
        IReadOnlyDictionary<string, EffectivenessProfile>? profile)
    {
        return new OperationalWorkflow
        {
            Id          = NewId(),
            Title       = "Behavioral Anomaly Investigation",
            ProblemSummary  = candidate.Explanation.Length > 0
                ? candidate.Explanation
                : "A behavioral anomaly was detected — system metrics are outside normal operating range.",
            EvidenceSummary = BuildEvidenceSummary(candidate),
            RiskSummary     = "Behavioral anomalies can indicate temporary workload spikes, background task interference, " +
                              "or early signs of a more significant issue.",
            Steps           = [
                new WorkflowStep
                {
                    StepNumber       = 1,
                    StepTitle        = "Open Task Manager",
                    StepDescription  = "Identify which processes are active and consuming unusual amounts of resources.",
                    ActionId         = "action.cpu.open-task-manager",
                    ExpectedEffect   = "Provides a real-time view of resource consumption by process.",
                    IsOptional       = false,
                },
                new WorkflowStep
                {
                    StepNumber       = 2,
                    StepTitle        = "Monitor for 3–5 minutes",
                    StepDescription  = "Watch whether the anomaly persists or self-resolves. " +
                                       "Many short-term anomalies are caused by background tasks (indexing, antivirus, updates) " +
                                       "that complete on their own.",
                    ActionId         = string.Empty,
                    ExpectedEffect   = "Anomaly should resolve if it was caused by a transient background task.",
                    IsOptional       = false,
                },
            ],
            ExpectedOutcome  = "If the anomaly was transient, metrics should return to normal baseline within 5 minutes.",
            FallbackActions  = [
                "If the anomaly persists, investigate the specific metric on its dedicated page.",
                "Check the Intelligence findings for more specific guidance.",
            ],
            HistoricalSuccessRate = GetSuccessRate(profile, string.Empty),
            Priority              = WorkflowPriority.Medium,
            CreatedAt             = DateTimeOffset.Now,
        };
    }

    private static OperationalWorkflow BuildGenericWorkflow(
        RootCauseCandidate                                  candidate,
        IReadOnlyDictionary<string, EffectivenessProfile>? profile)
    {
        return new OperationalWorkflow
        {
            Id          = NewId(),
            Title       = "System Investigation Workflow",
            ProblemSummary  = candidate.Explanation.Length > 0
                ? candidate.Explanation
                : "The intelligence system detected an operational condition worth investigating.",
            EvidenceSummary = BuildEvidenceSummary(candidate),
            RiskSummary     = "The operational condition detected may affect system performance or stability if not addressed.",
            Steps           = [
                new WorkflowStep
                {
                    StepNumber       = 1,
                    StepTitle        = "Review Intelligence findings",
                    StepDescription  = "Open the Insights page to review all active intelligence findings and their recommended actions.",
                    ActionId         = string.Empty,
                    ExpectedEffect   = "All active findings and recommendations are visible.",
                    IsOptional       = false,
                },
                new WorkflowStep
                {
                    StepNumber       = 2,
                    StepTitle        = "Identify highest-priority finding",
                    StepDescription  = "Focus on the finding with the highest severity. Follow its recommended action to address it.",
                    ActionId         = string.Empty,
                    ExpectedEffect   = "The primary concern is addressed, which may resolve secondary findings.",
                    IsOptional       = false,
                },
            ],
            ExpectedOutcome  = "Addressing the highest-priority finding should improve overall system health.",
            FallbackActions  = [
                "Run the Explain My PC analysis for a holistic view of current conditions.",
                "Check the Timeline page for recent events that may have triggered this condition.",
            ],
            HistoricalSuccessRate = 0.0,
            Priority              = WorkflowPriority.Low,
            CreatedAt             = DateTimeOffset.Now,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildEvidenceSummary(RootCauseCandidate candidate)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"This workflow was triggered with {candidate.ConfidenceLabel} ({candidate.Confidence:0%}) " +
                  $"based on {candidate.SupportingEvidence.Count} evidence item{(candidate.SupportingEvidence.Count == 1 ? "" : "s")}.");

        if (candidate.AffectedSystems.Count > 0)
            sb.Append($" Affected areas: {string.Join(", ", candidate.AffectedSystems)}.");

        return sb.ToString();
    }

    private static double GetSuccessRate(
        IReadOnlyDictionary<string, EffectivenessProfile>? profiles,
        string                                              actionKey)
    {
        if (profiles is null || string.IsNullOrEmpty(actionKey)) return 0.0;
        return profiles.TryGetValue(actionKey, out var p) ? p.SuccessRate : 0.0;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
