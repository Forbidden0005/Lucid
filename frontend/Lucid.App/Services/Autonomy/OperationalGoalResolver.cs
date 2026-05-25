namespace Lucid.Services.Autonomy;

/// <summary>
/// Converts plain-text conversational requests into structured <see cref="OperationalGoal"/>
/// objects that the workflow engine can plan and execute.
///
/// Resolution is deterministic keyword matching — NOT an LLM. Every resolved goal
/// is accompanied by a confidence score (0–100) so the engine can decide whether
/// to ask for clarification or proceed.
///
/// A confidence below 60 returns <see cref="OperationalGoalType.Unknown"/> and the
/// engine will produce a "couldn't determine goal" response.
///
/// All methods are synchronous and pure. Thread-safe.
/// </summary>
public sealed class OperationalGoalResolver
{
    // ── Keyword tables ────────────────────────────────────────────────────────

    /// <summary>
    /// Ordered priority list of pattern → goal type mappings.
    /// Earlier entries take precedence when multiple patterns match.
    /// </summary>
    private static readonly (string[] Keywords, OperationalGoalType GoalType, int BaseConfidence)[] _patterns =
    [
        // ── Downloads organization ────────────────────────────────────────────
        (["clean downloads", "organize downloads", "sort downloads",
          "tidy downloads", "cleanup downloads", "clear downloads"],
         OperationalGoalType.DownloadsOrganization, 95),

        // ── Screenshot archival ───────────────────────────────────────────────
        (["archive screenshots", "organize screenshots", "sort screenshots",
          "clean screenshots", "screenshot archive", "screenshot cleanup"],
         OperationalGoalType.ScreenshotArchival, 95),

        // ── Duplicate cleanup ─────────────────────────────────────────────────
        (["duplicate files", "find duplicates", "remove duplicates",
          "delete duplicates", "duplicate cleanup", "duplicate videos",
          "duplicate photos", "find duplicate"],
         OperationalGoalType.DuplicateFileCleanup, 93),

        // ── Media categorization ──────────────────────────────────────────────
        (["organize media", "sort media", "categorize videos",
          "organize photos", "sort photos", "organize videos"],
         OperationalGoalType.MediaCategorization, 88),

        // ── Old downloads cleanup ─────────────────────────────────────────────
        (["old downloads", "remove old downloads", "clean old files",
          "old files downloads"],
         OperationalGoalType.OldDownloadsCleanup, 90),

        // ── General folder organization ───────────────────────────────────────
        (["organize folder", "sort folder", "clean folder",
          "tidy folder", "organize files"],
         OperationalGoalType.GeneralFolderOrganization, 80),

        // ── Project discovery ─────────────────────────────────────────────────
        (["find project", "locate project", "where is my project",
          "find my unreal", "find my unity", "find my blender",
          "find my photoshop", "my project files", "project folder",
          "where are my projects"],
         OperationalGoalType.ProjectDiscovery, 92),

        // ── Large file discovery ──────────────────────────────────────────────
        (["find large files", "large files", "biggest files",
          "what is taking space", "what's using space",
          "find large videos", "large videos"],
         OperationalGoalType.LargeFileDiscovery, 90),

        // ── Recent file discovery ─────────────────────────────────────────────
        (["recent files", "recently modified", "recently changed",
          "last edited", "recently downloaded", "recent downloads"],
         OperationalGoalType.RecentFilesDiscovery, 88),

        // ── Folder size discovery ─────────────────────────────────────────────
        (["folder size", "what folder", "which folder is biggest",
          "folder taking space", "folder sizes"],
         OperationalGoalType.FolderSizeDiscovery, 85),

        // ── Startup investigation ─────────────────────────────────────────────
        (["slow startup", "slow boot", "startup slow", "fix startup",
          "startup apps", "startup programs", "startup taking long",
          "computer slow to start", "boot slow", "slow to start",
          "improve startup", "startup investigation"],
         OperationalGoalType.StartupInvestigation, 95),

        // ── Performance investigation ─────────────────────────────────────────
        (["slow computer", "slow pc", "why is my pc slow", "cpu high",
          "high cpu", "computer slow", "performance issue",
          "running slow", "everything is slow", "lagging"],
         OperationalGoalType.PerformanceInvestigation, 93),

        // ── Storage investigation ─────────────────────────────────────────────
        (["disk full", "running out of space", "low disk space",
          "disk space", "storage full", "not enough space",
          "free up space", "storage investigation"],
         OperationalGoalType.StorageInvestigation, 93),

        // ── Network investigation ─────────────────────────────────────────────
        (["network slow", "internet slow", "wifi slow", "connection issue",
          "can't connect", "network problem", "dns issue", "network fix"],
         OperationalGoalType.NetworkInvestigation, 90),

        // ── Repair workflow ───────────────────────────────────────────────────
        (["repair windows", "fix windows", "sfc scan", "dism",
          "system repair", "windows repair", "corrupted files",
          "fix system files"],
         OperationalGoalType.RepairWorkflow, 92),

        // ── Temp file cleanup ─────────────────────────────────────────────────
        (["clean temp", "temp files", "temporary files",
          "clear temp", "remove temp"],
         OperationalGoalType.TempFileCleanup, 90),

        // ── Cache cleanup ─────────────────────────────────────────────────────
        (["clear cache", "clean cache", "browser cache",
          "windows cache", "cache cleanup"],
         OperationalGoalType.CacheCleanup, 88),

        // ── Cleanup guide ─────────────────────────────────────────────────────
        (["clean up my pc", "cleanup my computer", "spring clean",
          "general cleanup", "clean up", "tidy up"],
         OperationalGoalType.CleanupGuide, 82),

        // ── Maintenance guide ─────────────────────────────────────────────────
        (["maintenance", "tune up", "optimize pc", "pc health",
          "keep pc healthy"],
         OperationalGoalType.MaintenanceGuide, 80),
    ];

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a plain-text request to an <see cref="OperationalGoal"/>.
    ///
    /// Returns a goal with <see cref="OperationalGoalType.Unknown"/> and confidence 0
    /// if the request does not match any supported pattern.
    /// </summary>
    public OperationalGoal Resolve(string request)
    {
        if (string.IsNullOrWhiteSpace(request))
            return BuildUnknown(request ?? string.Empty, 0);

        var lower = request.Trim().ToLowerInvariant();

        // Try each pattern in priority order
        foreach (var (keywords, goalType, baseConfidence) in _patterns)
        {
            foreach (var kw in keywords)
            {
                if (lower.Contains(kw, StringComparison.Ordinal))
                {
                    // Boost confidence for exact matches
                    var confidence = lower == kw ? 99 : baseConfidence;
                    return BuildGoal(goalType, request, confidence);
                }
            }
        }

        // Fuzzy fallback — check individual words
        return FuzzyFallback(lower, request);
    }

    /// <summary>
    /// Returns true if the given request maps to any supported goal with confidence ≥ 60.
    /// </summary>
    public bool CanResolve(string request)
    {
        var goal = Resolve(request);
        return goal.Type != OperationalGoalType.Unknown && goal.Confidence >= 60;
    }

    // ── Goal metadata ─────────────────────────────────────────────────────────

    private static OperationalGoal BuildGoal(OperationalGoalType type, string request, int confidence)
    {
        var (title, description) = MetadataFor(type);
        return new OperationalGoal
        {
            Id              = OperationalGoal.NewId(),
            Type            = type,
            Title           = title,
            Description     = description,
            Confidence      = confidence,
            OriginalRequest = request,
        };
    }

    private static OperationalGoal BuildUnknown(string request, int confidence) =>
        new()
        {
            Id              = OperationalGoal.NewId(),
            Type            = OperationalGoalType.Unknown,
            Title           = "Unknown goal",
            Description     = "The request could not be mapped to a supported workflow.",
            Confidence      = confidence,
            OriginalRequest = request,
        };

    private static (string Title, string Description) MetadataFor(OperationalGoalType type) => type switch
    {
        OperationalGoalType.DownloadsOrganization =>
            ("Organise Downloads folder",
             "Review and organise the Downloads folder by grouping and archiving older files."),

        OperationalGoalType.ScreenshotArchival =>
            ("Archive screenshots",
             "Move older screenshots into an organised archive subfolder."),

        OperationalGoalType.DuplicateFileCleanup =>
            ("Find and remove duplicate files",
             "Identify duplicate files and prepare a cleanup preview."),

        OperationalGoalType.MediaCategorization =>
            ("Categorise media files",
             "Group photos, videos, and audio into organised folders."),

        OperationalGoalType.OldDownloadsCleanup =>
            ("Clean old downloads",
             "Remove or archive downloads older than 90 days that haven't been opened recently."),

        OperationalGoalType.GeneralFolderOrganization =>
            ("Organise folder",
             "Review and organise files in the target folder by type and age."),

        OperationalGoalType.ProjectDiscovery =>
            ("Find project files",
             "Locate project folders based on file structure and recent activity."),

        OperationalGoalType.LargeFileDiscovery =>
            ("Find large files",
             "Surface files consuming the most disk space in user folders."),

        OperationalGoalType.RecentFilesDiscovery =>
            ("Show recent files",
             "Surface recently modified or downloaded files."),

        OperationalGoalType.FolderSizeDiscovery =>
            ("Show folder sizes",
             "Analyse which folders are consuming the most disk space."),

        OperationalGoalType.StartupInvestigation =>
            ("Investigate startup slowdown",
             "Identify startup applications and services contributing to slow boot time."),

        OperationalGoalType.PerformanceInvestigation =>
            ("Investigate performance issue",
             "Identify processes and conditions contributing to high CPU/RAM usage."),

        OperationalGoalType.StorageInvestigation =>
            ("Investigate low disk space",
             "Identify what is consuming disk space and surface cleanup opportunities."),

        OperationalGoalType.NetworkInvestigation =>
            ("Investigate network issue",
             "Open network diagnostics and surface relevant troubleshooting steps."),

        OperationalGoalType.RepairWorkflow =>
            ("Windows repair workflow",
             "Run SFC and DISM to check and repair Windows system files."),

        OperationalGoalType.TempFileCleanup =>
            ("Clear temporary files",
             "Remove accumulated temporary files to free disk space and reduce I/O pressure."),

        OperationalGoalType.CacheCleanup =>
            ("Clear system caches",
             "Clear Windows and browser caches to free disk space."),

        OperationalGoalType.CleanupGuide =>
            ("General PC cleanup",
             "Walk through the most impactful cleanup steps for this system."),

        OperationalGoalType.MaintenanceGuide =>
            ("PC maintenance guide",
             "Review system health and surface maintenance recommendations."),

        _ =>
            ("Unknown workflow", "The requested workflow is not yet supported."),
    };

    // ── Fuzzy fallback ────────────────────────────────────────────────────────

    private static OperationalGoal FuzzyFallback(string lower, string original)
    {
        // Single-word content hints
        if (lower.Contains("download"))   return BuildGoal(OperationalGoalType.DownloadsOrganization,  original, 65);
        if (lower.Contains("screenshot")) return BuildGoal(OperationalGoalType.ScreenshotArchival,     original, 65);
        if (lower.Contains("startup"))    return BuildGoal(OperationalGoalType.StartupInvestigation,   original, 65);
        if (lower.Contains("slow"))       return BuildGoal(OperationalGoalType.PerformanceInvestigation, original, 62);
        if (lower.Contains("space"))      return BuildGoal(OperationalGoalType.StorageInvestigation,   original, 62);
        if (lower.Contains("duplicate"))  return BuildGoal(OperationalGoalType.DuplicateFileCleanup,   original, 65);
        if (lower.Contains("temp"))       return BuildGoal(OperationalGoalType.TempFileCleanup,        original, 65);
        if (lower.Contains("project"))    return BuildGoal(OperationalGoalType.ProjectDiscovery,       original, 62);
        if (lower.Contains("network"))    return BuildGoal(OperationalGoalType.NetworkInvestigation,   original, 62);
        if (lower.Contains("repair"))     return BuildGoal(OperationalGoalType.RepairWorkflow,         original, 65);

        return BuildUnknown(original, 0);
    }
}
