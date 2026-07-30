using Lucid.Services.Trust;

namespace Lucid.Services.Autonomy;

/// <summary>
/// Converts a resolved <see cref="OperationalGoal"/> into a fully inspectable
/// <see cref="AutonomousWorkflow"/> with all steps, permissions, risk levels,
/// expected outcomes, estimated duration, and reversibility metadata.
///
/// Planning is synchronous, deterministic, and pure — no I/O, no AI.
/// Every workflow can be inspected before a single step executes.
/// Thread-safe: all members are stateless.
/// </summary>
public sealed class WorkflowExecutionPlanner
{
    private static readonly string UserProfile = Environment.GetFolderPath(
        Environment.SpecialFolder.UserProfile);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Produces a complete <see cref="AutonomousWorkflow"/> for the given goal.
    /// The returned workflow is in <see cref="WorkflowLifecycleStatus.Planning"/> state.
    /// </summary>
    public AutonomousWorkflow PlanWorkflow(OperationalGoal goal)
    {
        var steps           = BuildSteps(goal);
        var highestRisk     = steps.Count > 0 ? steps.Max(s => s.Risk) : TrustRiskLevel.None;
        var isReversible    = steps.All(s => s.IsReversible || IsReadOnly(s.StepType));
        var estimatedSeconds = EstimateDuration(goal.Type, steps.Count);
        var expectedOutcome = ExpectedOutcomeFor(goal.Type);

        return new AutonomousWorkflow
        {
            Id               = AutonomousWorkflow.NewId(),
            Title            = goal.Title,
            Description      = goal.Description,
            Goal             = goal,
            Steps            = steps,
            HighestRisk      = highestRisk,
            IsReversible     = isReversible,
            ExpectedOutcome  = expectedOutcome,
            EstimatedSeconds = estimatedSeconds,
        };
    }

    // ── Step builders ─────────────────────────────────────────────────────────

    private static IReadOnlyList<WorkflowStep> BuildSteps(OperationalGoal goal) =>
        goal.Type switch
        {
            OperationalGoalType.DownloadsOrganization     => BuildDownloadsOrgSteps(goal),
            OperationalGoalType.ScreenshotArchival        => BuildScreenshotArchivalSteps(goal),
            OperationalGoalType.DuplicateFileCleanup      => BuildDuplicateCleanupSteps(goal),
            OperationalGoalType.MediaCategorization       => BuildMediaCategorizationSteps(goal),
            OperationalGoalType.OldDownloadsCleanup       => BuildOldDownloadsSteps(goal),
            OperationalGoalType.GeneralFolderOrganization => BuildGeneralFolderSteps(goal),
            OperationalGoalType.TempFileCleanup           => BuildTempCleanupSteps(goal),
            OperationalGoalType.CacheCleanup              => BuildCacheCleanupSteps(goal),
            OperationalGoalType.LargeFileDiscovery        => BuildLargeFileDiscoverySteps(goal),
            OperationalGoalType.RecentFilesDiscovery      => BuildRecentFilesSteps(goal),
            OperationalGoalType.FolderSizeDiscovery       => BuildFolderSizeSteps(goal),
            OperationalGoalType.ProjectDiscovery          => BuildProjectDiscoverySteps(goal),
            OperationalGoalType.StartupInvestigation      => BuildStartupInvestigationSteps(),
            OperationalGoalType.PerformanceInvestigation  => BuildPerformanceSteps(),
            OperationalGoalType.StorageInvestigation      => BuildStorageInvestigationSteps(),
            OperationalGoalType.NetworkInvestigation      => BuildNetworkInvestigationSteps(),
            OperationalGoalType.RepairWorkflow            => BuildRepairWorkflowSteps(),
            OperationalGoalType.CleanupGuide              => BuildCleanupGuideSteps(),
            OperationalGoalType.MaintenanceGuide          => BuildMaintenanceGuideSteps(),
            _                                             => [],
        };

    // ── Downloads organization ────────────────────────────────────────────────

    private static IReadOnlyList<WorkflowStep> BuildDownloadsOrgSteps(OperationalGoal goal)
    {
        var path = goal.Parameters.TryGetValue("path", out var p)
            ? p : Path.Combine(UserProfile, "Downloads");

        return
        [
            MakeStep("Scan Downloads folder",
                $"Read file names, sizes, and dates in {path}.",
                WorkflowStepType.Discovery, TrustRiskLevel.None, targetPath: path),

            MakeStep("Categorise files by type and age",
                "Group files: Installers, Documents, Images, Videos, Archives, Other. Flag items older than 90 days.",
                WorkflowStepType.Analysis, TrustRiskLevel.None),

            MakeStep("Build organisation preview",
                "Prepare a preview showing exactly where each file will be moved. Nothing changes yet.",
                WorkflowStepType.Preview, TrustRiskLevel.None, targetPath: path),

            MakeApprovalStep("Review and approve organisation plan",
                "Review the proposed file moves and approve to proceed. You can cancel at any time.",
                TrustRiskLevel.Medium),

            MakeStep("Organise files into subfolders",
                "Move files into labelled subfolders within Downloads.",
                WorkflowStepType.FileOperation, TrustRiskLevel.Medium,
                requiresApproval: true, isReversible: true,
                actionId: "autonomy.file.organize-downloads", targetPath: path),
        ];
    }

    // ── Screenshot archival ───────────────────────────────────────────────────

    private static IReadOnlyList<WorkflowStep> BuildScreenshotArchivalSteps(OperationalGoal goal)
    {
        var path = goal.Parameters.TryGetValue("path", out var p)
            ? p : DetectScreenshotsFolder();

        return
        [
            MakeStep("Find screenshots",
                $"Scan {path} for screenshot files by extension and naming pattern.",
                WorkflowStepType.Discovery, TrustRiskLevel.None, targetPath: path),

            MakeStep("Group screenshots by date",
                "Group screenshots by month and year to build an archive folder structure.",
                WorkflowStepType.Analysis, TrustRiskLevel.None),

            MakeStep("Build archive preview",
                "Prepare a preview of the proposed archive folder structure. Nothing changes yet.",
                WorkflowStepType.Preview, TrustRiskLevel.None, targetPath: path),

            MakeApprovalStep("Review and approve archive plan",
                "Review where screenshots will be archived. Approve to continue.",
                TrustRiskLevel.Low),

            MakeStep("Archive screenshots by date",
                "Move screenshots into dated subfolders.",
                WorkflowStepType.FileOperation, TrustRiskLevel.Low,
                requiresApproval: true, isReversible: true,
                actionId: "autonomy.file.archive-screenshots", targetPath: path),
        ];
    }

    // ── Duplicate cleanup ─────────────────────────────────────────────────────

    private static IReadOnlyList<WorkflowStep> BuildDuplicateCleanupSteps(OperationalGoal goal)
    {
        var path = goal.Parameters.TryGetValue("path", out var p)
            ? p : Path.Combine(UserProfile, "Downloads");

        return
        [
            MakeStep("Discover files for duplicate scan",
                $"List files in {path} to prepare for content comparison.",
                WorkflowStepType.Discovery, TrustRiskLevel.None, targetPath: path),

            MakeStep("Find duplicate files",
                "Compare files by name and size. Identical pairs are flagged as duplicates.",
                WorkflowStepType.Analysis, TrustRiskLevel.None),

            MakeStep("Build duplicate removal preview",
                "Prepare a preview showing which files will be sent to the Recycle Bin.",
                WorkflowStepType.Preview, TrustRiskLevel.None, targetPath: path),

            MakeApprovalStep("Review and approve duplicate removal",
                "Review each group of duplicates. Only files you approve will be removed.",
                TrustRiskLevel.Medium),

            MakeStep("Move duplicates to Recycle Bin",
                "Send selected duplicates to the Recycle Bin. You can restore from there if needed.",
                WorkflowStepType.FileOperation, TrustRiskLevel.Medium,
                requiresApproval: true, isReversible: true,
                actionId: "action.storage.delete-duplicate-group", targetPath: path),
        ];
    }

    // ── Media categorization ──────────────────────────────────────────────────

    private static IReadOnlyList<WorkflowStep> BuildMediaCategorizationSteps(OperationalGoal goal)
    {
        var path = goal.Parameters.TryGetValue("path", out var p)
            ? p : Path.Combine(UserProfile, "Downloads");

        return
        [
            MakeStep("Find media files",
                $"Scan {path} for images, videos, and audio files.",
                WorkflowStepType.Discovery, TrustRiskLevel.None, targetPath: path),

            MakeStep("Categorise media by type",
                "Group files into: Photos, Videos, Audio.",
                WorkflowStepType.Analysis, TrustRiskLevel.None),

            MakeStep("Build categorisation preview",
                "Prepare a preview of the proposed folder structure.",
                WorkflowStepType.Preview, TrustRiskLevel.None, targetPath: path),

            MakeApprovalStep("Review and approve media organisation",
                "Review the proposed folder structure before any files are moved.",
                TrustRiskLevel.Medium),

            MakeStep("Organise media into category folders",
                "Move media files into Photos, Videos, and Audio subfolders.",
                WorkflowStepType.FileOperation, TrustRiskLevel.Medium,
                requiresApproval: true, isReversible: true,
                actionId: "autonomy.file.categorize-media", targetPath: path),
        ];
    }

    // ── Old downloads cleanup ─────────────────────────────────────────────────

    private static IReadOnlyList<WorkflowStep> BuildOldDownloadsSteps(OperationalGoal goal)
    {
        var path = goal.Parameters.TryGetValue("path", out var p)
            ? p : Path.Combine(UserProfile, "Downloads");

        return
        [
            MakeStep("Find old downloads",
                $"List files in {path} not modified in the last 90 days.",
                WorkflowStepType.Discovery, TrustRiskLevel.None, targetPath: path),

            MakeStep("Analyse download ages",
                "Identify files older than 90 days that are candidates for removal.",
                WorkflowStepType.Analysis, TrustRiskLevel.None),

            MakeStep("Build cleanup preview",
                "Prepare a preview showing files that will be sent to the Recycle Bin.",
                WorkflowStepType.Preview, TrustRiskLevel.None, targetPath: path),

            MakeApprovalStep("Review and approve old download removal",
                "Review the list of old downloads before removal. All go to Recycle Bin.",
                TrustRiskLevel.Medium),

            MakeStep("Move old downloads to Recycle Bin",
                "Send old downloads to the Recycle Bin.",
                WorkflowStepType.FileOperation, TrustRiskLevel.Medium,
                requiresApproval: true, isReversible: true,
                actionId: "action.storage.clean-old-downloads", targetPath: path),
        ];
    }

    // ── General folder organization ───────────────────────────────────────────

    private static IReadOnlyList<WorkflowStep> BuildGeneralFolderSteps(OperationalGoal goal)
    {
        var path = goal.Parameters.TryGetValue("path", out var p) ? p : UserProfile;

        return
        [
            MakeStep("Scan folder",
                $"Read all files and subfolders in {path}.",
                WorkflowStepType.Discovery, TrustRiskLevel.None, targetPath: path),

            MakeStep("Analyse folder contents",
                "Identify file types, sizes, and ages to build an organisation plan.",
                WorkflowStepType.Analysis, TrustRiskLevel.None),

            MakeStep("Build organisation preview",
                "Prepare a preview of proposed file moves.",
                WorkflowStepType.Preview, TrustRiskLevel.None, targetPath: path),

            MakeApprovalStep("Review and approve folder organisation",
                "Review proposed changes before anything is moved.",
                TrustRiskLevel.Medium),

            MakeStep("Organise folder contents",
                "Move files as specified in the approved plan.",
                WorkflowStepType.FileOperation, TrustRiskLevel.Medium,
                requiresApproval: true, isReversible: true,
                actionId: "autonomy.file.organize-folder", targetPath: path),
        ];
    }

    // ── Temp file cleanup ─────────────────────────────────────────────────────

    private static IReadOnlyList<WorkflowStep> BuildTempCleanupSteps(OperationalGoal _) =>
    [
        MakeStep("Scan temporary file locations",
            "Identify temporary files in the system temp directory.",
            WorkflowStepType.Discovery, TrustRiskLevel.None),

        MakeStep("Estimate reclaimable space",
            "Calculate total size of temporary files that can be safely removed.",
            WorkflowStepType.Analysis, TrustRiskLevel.None),

        MakeApprovalStep("Confirm temporary file removal",
            "Approve to remove temporary files. Frees disk space — cannot be undone.",
            TrustRiskLevel.Low),

        MakeStep("Clean temporary files",
            "Remove accumulated temporary files to free disk space.",
            WorkflowStepType.SystemOperation, TrustRiskLevel.Low,
            requiresApproval: true, isReversible: false,
            actionId: "action.disk.clean-temp-files"),
    ];

    // ── Cache cleanup ─────────────────────────────────────────────────────────

    private static IReadOnlyList<WorkflowStep> BuildCacheCleanupSteps(OperationalGoal _) =>
    [
        MakeStep("Identify caches",
            "Locate browser caches and Windows update caches.",
            WorkflowStepType.Discovery, TrustRiskLevel.None),

        MakeStep("Estimate cache sizes",
            "Calculate the total reclaimable space across all identified caches.",
            WorkflowStepType.Analysis, TrustRiskLevel.None),

        MakeApprovalStep("Confirm cache clearing",
            "Approve to clear the caches listed above. Browser caches are safe to clear.",
            TrustRiskLevel.Low),

        MakeStep("Clear browser caches",
            "Remove accumulated browser cache files.",
            WorkflowStepType.SystemOperation, TrustRiskLevel.Low,
            requiresApproval: true, isReversible: false,
            actionId: "action.disk.clean-browser-cache"),
    ];

    // ── Discovery-only workflows ──────────────────────────────────────────────

    private static IReadOnlyList<WorkflowStep> BuildLargeFileDiscoverySteps(OperationalGoal goal)
    {
        var path = goal.Parameters.TryGetValue("path", out var p) ? p : UserProfile;

        return
        [
            MakeStep("Scan for large files",
                $"Find files over 100 MB in {path} and subfolders.",
                WorkflowStepType.Discovery, TrustRiskLevel.None, targetPath: path),

            MakeStep("Rank by size",
                "Sort results largest-first and group by file type.",
                WorkflowStepType.Analysis, TrustRiskLevel.None),

            MakeStep("Report large file findings",
                "Present the largest files with size, location, and age. No files are changed.",
                WorkflowStepType.Narration, TrustRiskLevel.None),
        ];
    }

    private static IReadOnlyList<WorkflowStep> BuildRecentFilesSteps(OperationalGoal goal)
    {
        var path = goal.Parameters.TryGetValue("path", out var p) ? p : UserProfile;

        return
        [
            MakeStep("Find recently modified files",
                $"List files modified in the last 7 days in {path}.",
                WorkflowStepType.Discovery, TrustRiskLevel.None, targetPath: path),

            MakeStep("Report recent activity",
                "Present recent files grouped by modification date. No files are changed.",
                WorkflowStepType.Narration, TrustRiskLevel.None),
        ];
    }

    private static IReadOnlyList<WorkflowStep> BuildFolderSizeSteps(OperationalGoal goal)
    {
        var path = goal.Parameters.TryGetValue("path", out var p) ? p : UserProfile;

        return
        [
            MakeStep("Measure folder sizes",
                $"Calculate total size of top-level folders in {path}.",
                WorkflowStepType.Discovery, TrustRiskLevel.None, targetPath: path),

            MakeStep("Rank folders by size",
                "Sort results to surface the biggest consumers.",
                WorkflowStepType.Analysis, TrustRiskLevel.None),

            MakeStep("Report folder size breakdown",
                "Present the folder size report. No files are changed.",
                WorkflowStepType.Narration, TrustRiskLevel.None),
        ];
    }

    private static IReadOnlyList<WorkflowStep> BuildProjectDiscoverySteps(OperationalGoal goal)
    {
        var path = goal.Parameters.TryGetValue("path", out var p) ? p : UserProfile;

        return
        [
            MakeStep("Search for project folders",
                $"Look for project indicators (.sln, .git, Cargo.toml, etc.) in {path}.",
                WorkflowStepType.Discovery, TrustRiskLevel.None, targetPath: path),

            MakeStep("Identify project types",
                "Classify each found project folder by type and estimate recency.",
                WorkflowStepType.Analysis, TrustRiskLevel.None),

            MakeStep("Report project findings",
                "Present discovered project folders with type, location, and last activity.",
                WorkflowStepType.Narration, TrustRiskLevel.None),
        ];
    }

    // ── Operational investigation workflows ───────────────────────────────────

    private static IReadOnlyList<WorkflowStep> BuildStartupInvestigationSteps() =>
    [
        MakeStep("Read startup entries",
            "Query Windows startup entries from the registry and startup folder.",
            WorkflowStepType.Discovery, TrustRiskLevel.None),

        MakeStep("Analyse startup impact",
            "Estimate the boot delay contribution of each startup item.",
            WorkflowStepType.Analysis, TrustRiskLevel.None),

        MakeStep("Report startup findings",
            "Present startup items ranked by impact with suggestions.",
            WorkflowStepType.Narration, TrustRiskLevel.None),

        MakeStep("Open Startup Apps manager",
            "Open Windows Startup Apps settings so you can disable items directly.",
            WorkflowStepType.Navigation, TrustRiskLevel.None,
            actionId: "action.startup.open-startup-apps"),
    ];

    private static IReadOnlyList<WorkflowStep> BuildPerformanceSteps() =>
    [
        MakeStep("Capture system state",
            "Record current CPU, RAM, and process metrics.",
            WorkflowStepType.Discovery, TrustRiskLevel.None),

        MakeStep("Identify high-resource processes",
            "Find processes contributing most to current resource pressure.",
            WorkflowStepType.Analysis, TrustRiskLevel.None),

        MakeStep("Report performance findings",
            "Present the top resource consumers with context and suggestions.",
            WorkflowStepType.Narration, TrustRiskLevel.None),

        MakeStep("Open Task Manager",
            "Open Task Manager for further investigation.",
            WorkflowStepType.Navigation, TrustRiskLevel.None,
            actionId: "action.cpu.open-task-manager"),
    ];

    private static IReadOnlyList<WorkflowStep> BuildStorageInvestigationSteps() =>
    [
        MakeStep("Scan storage usage",
            "Measure disk usage across top-level user folders.",
            WorkflowStepType.Discovery, TrustRiskLevel.None),

        MakeStep("Identify largest consumers",
            "Rank folders and file types consuming the most space.",
            WorkflowStepType.Analysis, TrustRiskLevel.None),

        MakeStep("Report storage findings",
            "Present a storage breakdown with cleanup opportunities.",
            WorkflowStepType.Narration, TrustRiskLevel.None),

        MakeStep("Open Storage Sense",
            "Open Windows Storage Sense for additional cleanup options.",
            WorkflowStepType.Navigation, TrustRiskLevel.None,
            actionId: "action.disk.run-storage-sense"),
    ];

    private static IReadOnlyList<WorkflowStep> BuildNetworkInvestigationSteps() =>
    [
        MakeStep("Check network adapters",
            "Enumerate active network adapters and their states.",
            WorkflowStepType.Discovery, TrustRiskLevel.None),

        MakeStep("Diagnose connectivity",
            "Test DNS resolution and gateway connectivity.",
            WorkflowStepType.Analysis, TrustRiskLevel.None),

        MakeStep("Report network findings",
            "Present network status with suggested repair steps.",
            WorkflowStepType.Narration, TrustRiskLevel.None),
    ];

    private static IReadOnlyList<WorkflowStep> BuildRepairWorkflowSteps() =>
    [
        MakeStep("Assess system file state",
            "Check for indicators that a Windows system file scan is warranted.",
            WorkflowStepType.Discovery, TrustRiskLevel.None),

        MakeApprovalStep("Confirm SFC scan",
            "SFC will scan and repair corrupted Windows files. Requires administrator privileges.",
            TrustRiskLevel.High),

        MakeStep("Run SFC scan",
            "Execute sfc /scannow to check and repair Windows system files.",
            WorkflowStepType.SystemOperation, TrustRiskLevel.High,
            requiresApproval: true, isReversible: false,
            actionId: "action.repair.sfc-scannow"),

        MakeStep("Report SFC results",
            "Present the scan results and surface any follow-up recommendations.",
            WorkflowStepType.Narration, TrustRiskLevel.None),
    ];

    private static IReadOnlyList<WorkflowStep> BuildCleanupGuideSteps() =>
    [
        MakeStep("Assess current state",
            "Capture a snapshot of disk usage, temp file volume, and cache sizes.",
            WorkflowStepType.Discovery, TrustRiskLevel.None),

        MakeStep("Identify cleanup opportunities",
            "Rank cleanup actions by potential space reclaimed.",
            WorkflowStepType.Analysis, TrustRiskLevel.None),

        MakeStep("Present cleanup guide",
            "Walk through the most impactful cleanup steps for this system.",
            WorkflowStepType.Narration, TrustRiskLevel.None),
    ];

    private static IReadOnlyList<WorkflowStep> BuildMaintenanceGuideSteps() =>
    [
        MakeStep("Check system health signals",
            "Review startup, storage, performance, and update status.",
            WorkflowStepType.Discovery, TrustRiskLevel.None),

        MakeStep("Evaluate maintenance needs",
            "Score each health area and identify recommended maintenance actions.",
            WorkflowStepType.Analysis, TrustRiskLevel.None),

        MakeStep("Present maintenance recommendations",
            "Surface the highest-value maintenance actions ranked by impact.",
            WorkflowStepType.Narration, TrustRiskLevel.None),
    ];

    // ── Step factories ────────────────────────────────────────────────────────

    private static WorkflowStep MakeStep(
        string           title,
        string           description,
        WorkflowStepType stepType,
        TrustRiskLevel   risk,
        bool             requiresApproval = false,
        bool?            isReversible     = null,
        string?          actionId         = null,
        string?          targetPath       = null) =>
        new()
        {
            Id               = WorkflowStep.NewId(),
            Title            = title,
            Description      = description,
            StepType         = stepType,
            Risk             = risk,
            RequiresApproval = requiresApproval,
            IsReversible     = isReversible ?? IsReadOnly(stepType),
            ActionId         = actionId,
            TargetPath       = targetPath,
        };

    private static WorkflowStep MakeApprovalStep(
        string title, string description, TrustRiskLevel risk) =>
        MakeStep(title, description, WorkflowStepType.RequiresApproval, risk,
            requiresApproval: true, isReversible: true);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsReadOnly(WorkflowStepType t) =>
        t is WorkflowStepType.Discovery
          or WorkflowStepType.Analysis
          or WorkflowStepType.Preview
          or WorkflowStepType.Narration;

    private static string DetectScreenshotsFolder()
    {
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        if (oneDrive is not null)
        {
            var od = Path.Combine(oneDrive, "Pictures", "Screenshots");
            if (Directory.Exists(od)) return od;
        }

        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var std      = Path.Combine(pictures, "Screenshots");
        return Directory.Exists(std) ? std : pictures;
    }

    private static int EstimateDuration(OperationalGoalType type, int stepCount) => type switch
    {
        OperationalGoalType.DuplicateFileCleanup     => 120,
        OperationalGoalType.RepairWorkflow           => 300,
        OperationalGoalType.PerformanceInvestigation => 30,
        OperationalGoalType.NetworkInvestigation     => 45,
        OperationalGoalType.StorageInvestigation     => 60,
        _                                            => stepCount * 15,
    };

    private static string ExpectedOutcomeFor(OperationalGoalType type) => type switch
    {
        OperationalGoalType.DownloadsOrganization     => "Downloads folder organised into labelled subfolders.",
        OperationalGoalType.ScreenshotArchival        => "Screenshots archived into dated folders.",
        OperationalGoalType.DuplicateFileCleanup      => "Duplicate files identified and removed.",
        OperationalGoalType.MediaCategorization       => "Media files organised into category folders.",
        OperationalGoalType.OldDownloadsCleanup       => "Old downloads cleared from the Downloads folder.",
        OperationalGoalType.GeneralFolderOrganization => "Folder contents organised by type and age.",
        OperationalGoalType.TempFileCleanup           => "Temporary files cleared to free disk space.",
        OperationalGoalType.CacheCleanup              => "System and browser caches cleared.",
        OperationalGoalType.LargeFileDiscovery        => "Largest files surfaced for review.",
        OperationalGoalType.RecentFilesDiscovery      => "Recently modified files presented.",
        OperationalGoalType.FolderSizeDiscovery       => "Folder sizes reported with largest identified.",
        OperationalGoalType.ProjectDiscovery          => "Project folders located and identified.",
        OperationalGoalType.StartupInvestigation      => "Startup items evaluated and ranked by impact.",
        OperationalGoalType.PerformanceInvestigation  => "High-resource processes identified with context.",
        OperationalGoalType.StorageInvestigation      => "Storage consumers identified with cleanup paths.",
        OperationalGoalType.NetworkInvestigation      => "Network status assessed with repair guidance.",
        OperationalGoalType.RepairWorkflow            => "Windows system file integrity checked and repaired.",
        OperationalGoalType.CleanupGuide              => "PC cleanup plan presented with prioritised steps.",
        OperationalGoalType.MaintenanceGuide          => "Maintenance recommendations surfaced.",
        _                                             => "Workflow completed.",
    };
}
