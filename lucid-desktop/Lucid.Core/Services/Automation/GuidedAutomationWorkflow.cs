namespace Lucid.Services.Automation;

/// <summary>
/// Library of pre-built guided automation workflows.
///
/// Each workflow is a named <see cref="AutomationExecutionPlan"/> tailored to
/// common operational goals (investigate CPU, clean storage, check security, etc.).
///
/// Workflows are:
///   • Deterministic — same goal always produces the same plan structure
///   • Conservative — minimal steps, lowest risk that achieves the goal
///   • Transparent — every step includes full narration and rationale
///   • Non-destructive by default — navigation/tool-open workflows have zero risk
///
/// Plans are built fresh on each call so callers always get current state.
/// </summary>
public sealed class GuidedAutomationWorkflow
{
    // ── Navigation workflows (zero-risk) ──────────────────────────────────────

    /// <summary>Plan: open Task Manager to investigate CPU or process usage.</summary>
    public AutomationExecutionPlan InvestigateCpu() => new()
    {
        Id              = AutomationExecutionPlan.NewId(),
        Goal            = "Open Task Manager to investigate CPU usage",
        Narration       = "I'll open Task Manager so you can see which processes are consuming the most CPU right now.",
        ExpectedOutcome = "Task Manager will open sorted by CPU usage so you can identify the top consumers.",
        Scope           = AutomationScope.SystemTool,
        HighestRisk     = AutomationRiskLevel.Low,
        RequiresConfirmation = false,
        RollbackAvailable    = false,
        EstimatedSeconds     = 2,
        Confidence           = 95,
        Steps =
        [
            new AutomationStep
            {
                Id           = AutomationStep.NewId(),
                Title        = "Open Task Manager",
                Narration    = "Opening Task Manager so you can see live CPU, memory, and disk usage by process.",
                Rationale    = "Task Manager is the fastest way to identify which process is driving high CPU.",
                ActionId     = "automation.launch.task-manager",
                Risk         = AutomationRiskLevel.Low,
                RequiresConfirmation = false,
                RollbackAvailable    = false,
                ExpectedOutcome = "Task Manager opens on the Processes tab.",
            },
        ],
    };

    /// <summary>Plan: open Task Manager's Startup tab to review startup items.</summary>
    public AutomationExecutionPlan ReviewStartupApps() => new()
    {
        Id              = AutomationExecutionPlan.NewId(),
        Goal            = "Review startup apps that slow down boot time",
        Narration       = "I'll open the Startup Apps settings so you can review which apps launch at login and disable the ones you don't need.",
        ExpectedOutcome = "The Startup Apps settings page will open, showing each app's startup impact.",
        Scope           = AutomationScope.SystemTool,
        HighestRisk     = AutomationRiskLevel.Low,
        RequiresConfirmation = false,
        RollbackAvailable    = false,
        EstimatedSeconds     = 2,
        Confidence           = 95,
        Steps =
        [
            new AutomationStep
            {
                Id           = AutomationStep.NewId(),
                Title        = "Open Startup Apps settings",
                Narration    = "Opening the Startup Apps settings page where you can see and manage which apps run at login.",
                Rationale    = "Startup apps with 'High' impact are the most common cause of slow boot times.",
                ActionId     = "automation.launch.startup-apps",
                Risk         = AutomationRiskLevel.Low,
                RequiresConfirmation = false,
                RollbackAvailable    = false,
                ExpectedOutcome = "Startup Apps settings opens, showing each app's enabled state and impact rating.",
            },
        ],
    };

    /// <summary>Plan: open Storage settings and Downloads folder to investigate disk usage.</summary>
    public AutomationExecutionPlan InvestigateStorage() => new()
    {
        Id              = AutomationExecutionPlan.NewId(),
        Goal            = "Investigate disk usage and open storage tools",
        Narration       = "I'll open the Storage settings page and your Downloads folder so you can see what's taking up space.",
        ExpectedOutcome = "Storage settings will show a breakdown by category, and Downloads folder will open for review.",
        Scope           = AutomationScope.Navigation,
        HighestRisk     = AutomationRiskLevel.Low,
        RequiresConfirmation = false,
        RollbackAvailable    = false,
        EstimatedSeconds     = 3,
        Confidence           = 95,
        Steps =
        [
            new AutomationStep
            {
                Id           = AutomationStep.NewId(),
                Title        = "Open Storage settings",
                Narration    = "Opening Windows Storage settings to show you which categories are using the most disk space.",
                Rationale    = "Storage settings provides a quick breakdown of what's using your disk without any scanning.",
                ActionId     = "automation.launch.storage-settings",
                Risk         = AutomationRiskLevel.Low,
                RequiresConfirmation = false,
                RollbackAvailable    = false,
                ExpectedOutcome = "Storage settings opens with a pie-chart breakdown by category.",
            },
            new AutomationStep
            {
                Id           = AutomationStep.NewId(),
                Title        = "Open Downloads folder",
                Narration    = "Opening your Downloads folder — it's often the biggest source of recoverable disk space.",
                Rationale    = "Downloads folders often accumulate installers, archives, and large files that may be good candidates for deletion once reviewed.",
                ActionId     = "automation.explorer.downloads",
                Risk         = AutomationRiskLevel.None,
                RequiresConfirmation = false,
                RollbackAvailable    = false,
                ExpectedOutcome = "Your Downloads folder opens in File Explorer for manual review.",
            },
        ],
    };

    /// <summary>Plan: open Windows Security to review antivirus and firewall status.</summary>
    public AutomationExecutionPlan ReviewSecurity() => new()
    {
        Id              = AutomationExecutionPlan.NewId(),
        Goal            = "Open Windows Security to review protection status",
        Narration       = "I'll open Windows Security so you can see your antivirus and firewall status in one place.",
        ExpectedOutcome = "Windows Security will open showing the current protection status across all categories.",
        Scope           = AutomationScope.SystemTool,
        HighestRisk     = AutomationRiskLevel.Low,
        RequiresConfirmation = false,
        RollbackAvailable    = false,
        EstimatedSeconds     = 2,
        Confidence           = 95,
        Steps =
        [
            new AutomationStep
            {
                Id           = AutomationStep.NewId(),
                Title        = "Open Windows Security",
                Narration    = "Opening Windows Security to show you the current protection status.",
                Rationale    = "Windows Security is the authoritative source for antivirus, firewall, and account protection status.",
                ActionId     = "automation.launch.windows-security",
                Risk         = AutomationRiskLevel.Low,
                RequiresConfirmation = false,
                RollbackAvailable    = false,
                ExpectedOutcome = "Windows Security opens on the Home screen showing protection status.",
            },
        ],
    };

    /// <summary>Plan: open Device Manager to check for driver issues.</summary>
    public AutomationExecutionPlan CheckDrivers() => new()
    {
        Id              = AutomationExecutionPlan.NewId(),
        Goal            = "Open Device Manager to check for driver issues",
        Narration       = "I'll open Device Manager so you can see if any devices have driver errors or warnings.",
        ExpectedOutcome = "Device Manager will open. Devices with issues show a yellow warning or red error icon.",
        Scope           = AutomationScope.SystemTool,
        HighestRisk     = AutomationRiskLevel.Low,
        RequiresConfirmation = false,
        RollbackAvailable    = false,
        EstimatedSeconds     = 2,
        Confidence           = 95,
        Steps =
        [
            new AutomationStep
            {
                Id           = AutomationStep.NewId(),
                Title        = "Open Device Manager",
                Narration    = "Opening Device Manager to display a tree of all hardware and their driver status.",
                Rationale    = "Device Manager is the definitive source for driver health — yellow banners indicate problems.",
                ActionId     = "automation.launch.device-manager",
                Risk         = AutomationRiskLevel.Low,
                RequiresConfirmation = false,
                RollbackAvailable    = false,
                ExpectedOutcome = "Device Manager opens. Look for yellow warning triangles — those indicate driver problems.",
            },
        ],
    };

    /// <summary>Plan: open Event Viewer to check for recent system errors.</summary>
    public AutomationExecutionPlan ReviewEventLog() => new()
    {
        Id              = AutomationExecutionPlan.NewId(),
        Goal            = "Open Event Viewer to review recent system errors",
        Narration       = "I'll open Event Viewer so you can see recent system and application errors logged by Windows.",
        ExpectedOutcome = "Event Viewer will open. Navigate to Windows Logs → System to see recent errors.",
        Scope           = AutomationScope.SystemTool,
        HighestRisk     = AutomationRiskLevel.Low,
        RequiresConfirmation = false,
        RollbackAvailable    = false,
        EstimatedSeconds     = 2,
        Confidence           = 90,
        Steps =
        [
            new AutomationStep
            {
                Id           = AutomationStep.NewId(),
                Title        = "Open Event Viewer",
                Narration    = "Opening Event Viewer to show Windows system and application logs.",
                Rationale    = "Event Viewer captures errors and warnings from Windows and installed software that are not visible anywhere else.",
                ActionId     = "automation.launch.event-viewer",
                Risk         = AutomationRiskLevel.Low,
                RequiresConfirmation = false,
                RollbackAvailable    = false,
                ExpectedOutcome = "Event Viewer opens. Go to Windows Logs → System, then filter by Error level to see recent issues.",
            },
        ],
    };

    /// <summary>Plan: open the Repairs page in Lucid.</summary>
    public AutomationExecutionPlan OpenLucidRepairs() => new()
    {
        Id              = AutomationExecutionPlan.NewId(),
        Goal            = "Navigate to the Repairs page",
        Narration       = "I'll take you to the Repairs page where you can run safe system maintenance tools.",
        ExpectedOutcome = "The Repairs page will open, showing available repair and maintenance options.",
        Scope           = AutomationScope.Navigation,
        HighestRisk     = AutomationRiskLevel.None,
        RequiresConfirmation = false,
        RollbackAvailable    = false,
        EstimatedSeconds     = 1,
        Confidence           = 99,
        Steps =
        [
            new AutomationStep
            {
                Id           = AutomationStep.NewId(),
                Title        = "Open Repairs page",
                Narration    = "Navigating to the Repairs page.",
                Rationale    = "The Repairs page provides access to SFC, DISM, DNS flush, and other safe Windows maintenance tools.",
                ActionId     = "automation.navigate",
                ActionParams = "repairs",
                Risk         = AutomationRiskLevel.None,
                RequiresConfirmation = false,
                RollbackAvailable    = false,
            },
        ],
    };

    // ── Plan factory (route by goal keyword) ──────────────────────────────────

    /// <summary>
    /// Returns the best pre-built workflow for the given goal keyword.
    /// Returns null if no matching workflow exists — the orchestrator will
    /// fall back to a single-step navigation plan.
    /// </summary>
    public AutomationExecutionPlan? FromGoalKeyword(string goalKeyword) =>
        goalKeyword.ToLowerInvariant() switch
        {
            var k when k.Contains("cpu") || k.Contains("slow") || k.Contains("process")
                => InvestigateCpu(),
            var k when k.Contains("startup") || k.Contains("boot") || k.Contains("login")
                => ReviewStartupApps(),
            var k when k.Contains("storage") || k.Contains("disk") || k.Contains("space") || k.Contains("download")
                => InvestigateStorage(),
            var k when k.Contains("security") || k.Contains("virus") || k.Contains("defender") || k.Contains("firewall")
                => ReviewSecurity(),
            var k when k.Contains("driver") || k.Contains("device")
                => CheckDrivers(),
            var k when k.Contains("event") || k.Contains("error") || k.Contains("log")
                => ReviewEventLog(),
            var k when k.Contains("repair") || k.Contains("fix") || k.Contains("maintenance")
                => OpenLucidRepairs(),
            _ => null,
        };
}
