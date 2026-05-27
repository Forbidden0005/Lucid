namespace Lucid.Services.Autonomy;

/// <summary>
/// Performs read-only file system discovery for autonomous workflow steps.
///
/// Each discovery method is scoped to the goal — no excess I/O.
/// Discovery never modifies files. All methods are safe to call from the thread pool.
/// </summary>
public sealed class OperationalFileDiscoveryService
{
    private readonly SafeExecutionValidator _validator;

    public OperationalFileDiscoveryService(SafeExecutionValidator validator)
    {
        _validator = validator;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the appropriate discovery for the given goal type and target path.
    /// Returns a <see cref="FileDiscoveryResult"/> — always returns, never throws.
    /// </summary>
    public async Task<FileDiscoveryResult> DiscoverAsync(
        OperationalGoalType goalType,
        string?             targetPath,
        CancellationToken   ct = default)
    {
        await Task.Yield(); // ensure we're off the UI thread

        var scanPath = targetPath ?? Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);

        if (_validator.ValidatePath(scanPath) is not null)
            return EmptyResult(goalType, "Path is in a protected location.");

        try
        {
            var (files, folders) = goalType switch
            {
                OperationalGoalType.LargeFileDiscovery   => await DiscoverLargeFilesAsync(scanPath, ct),
                OperationalGoalType.RecentFilesDiscovery => await DiscoverRecentFilesAsync(scanPath, ct),
                OperationalGoalType.FolderSizeDiscovery  => await DiscoverFolderSizesAsync(scanPath, ct),
                OperationalGoalType.ProjectDiscovery     => await DiscoverProjectsAsync(scanPath, ct),
                OperationalGoalType.DuplicateFileCleanup => await DiscoverDuplicatesAsync(scanPath, ct),
                OperationalGoalType.ScreenshotArchival   => await DiscoverScreenshotsAsync(scanPath, ct),
                OperationalGoalType.MediaCategorization  => await DiscoverMediaAsync(scanPath, ct),
                _                                        => await DiscoverGeneralAsync(scanPath, ct),
            };

            return new FileDiscoveryResult
            {
                Id               = FileDiscoveryResult.NewId(),
                GoalType         = goalType,
                Files            = files,
                Folders          = folders,
                SummaryNarration = BuildNarration(goalType, files.Count, folders.Count),
                DiscoveredAt     = DateTimeOffset.Now,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return EmptyResult(goalType, "Discovery could not complete.");
        }
    }

    // ── Discovery methods ─────────────────────────────────────────────────────

    private static async Task<(List<DiscoveredFile>, List<DiscoveredFolder>)>
        DiscoverLargeFilesAsync(string path, CancellationToken ct)
    {
        await Task.Yield();
        const long threshold = 100L * 1024 * 1024; // 100 MB
        var files = new List<DiscoveredFile>();

        foreach (var filePath in SafeEnumerateFiles(path, SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(filePath);
                if (info.Length < threshold) continue;

                files.Add(new DiscoveredFile
                {
                    Path            = filePath,
                    Name            = info.Name,
                    SizeBytes       = info.Length,
                    LastModified    = new DateTimeOffset(info.LastWriteTime),
                    Category        = GetCategory(Path.GetExtension(filePath)),
                    RelevanceReason = $"{FormatBytes(info.Length)} — large file",
                });
            }
            catch { }
        }

        return (files.OrderByDescending(f => f.SizeBytes).Take(50).ToList(), []);
    }

    private static async Task<(List<DiscoveredFile>, List<DiscoveredFolder>)>
        DiscoverRecentFilesAsync(string path, CancellationToken ct)
    {
        await Task.Yield();
        var cutoff = DateTime.Now.AddDays(-7);
        var files  = new List<DiscoveredFile>();

        foreach (var filePath in SafeEnumerateFiles(path, SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(filePath);
                if (info.LastWriteTime < cutoff) continue;

                files.Add(new DiscoveredFile
                {
                    Path            = filePath,
                    Name            = info.Name,
                    SizeBytes       = info.Length,
                    LastModified    = new DateTimeOffset(info.LastWriteTime),
                    RelevanceReason = $"Modified {info.LastWriteTime:g}",
                });
            }
            catch { }
        }

        return (files.OrderByDescending(f => f.LastModified).Take(30).ToList(), []);
    }

    private static async Task<(List<DiscoveredFile>, List<DiscoveredFolder>)>
        DiscoverFolderSizesAsync(string path, CancellationToken ct)
    {
        await Task.Yield();
        var folders = new List<DiscoveredFolder>();

        foreach (var dir in SafeEnumerateDirectories(path))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var (size, count) = MeasureFolder(dir);
                folders.Add(new DiscoveredFolder
                {
                    Path            = dir,
                    Name            = Path.GetFileName(dir),
                    RelevanceReason = $"{FormatBytes(size)} in {count} files",
                    FileCount       = count,
                    TotalBytes      = size,
                });
            }
            catch { }
        }

        return ([], folders.OrderByDescending(f => f.TotalBytes).Take(20).ToList());
    }

    private static async Task<(List<DiscoveredFile>, List<DiscoveredFolder>)>
        DiscoverProjectsAsync(string path, CancellationToken ct)
    {
        await Task.Yield();

        // Well-known project indicator patterns
        string[] indicators =
        [
            "*.sln", "*.csproj", "Cargo.toml", "package.json",
            "CMakeLists.txt", "*.xcodeproj", "build.gradle", "*.uproject",
        ];

        var folders = new List<DiscoveredFolder>();

        foreach (var dir in SafeEnumerateDirectories(path, SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var indicator in indicators)
            {
                try
                {
                    if (!Directory.EnumerateFiles(dir, indicator,
                            SearchOption.TopDirectoryOnly).Any()) continue;

                    folders.Add(new DiscoveredFolder
                    {
                        Path            = dir,
                        Name            = Path.GetFileName(dir),
                        RelevanceReason = $"Project folder (contains {indicator})",
                    });
                    break;
                }
                catch { }
            }
        }

        return ([], folders.Take(20).ToList());
    }

    private static async Task<(List<DiscoveredFile>, List<DiscoveredFolder>)>
        DiscoverDuplicatesAsync(string path, CancellationToken ct)
    {
        await Task.Yield();
        var bySize = new Dictionary<long, List<string>>();
        var files  = new List<DiscoveredFile>();

        foreach (var filePath in SafeEnumerateFiles(path, SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(filePath);
                if (!bySize.TryGetValue(info.Length, out var list))
                    bySize[info.Length] = list = [];
                list.Add(filePath);
            }
            catch { }
        }

        foreach (var (size, paths) in bySize)
        {
            if (paths.Count < 2) continue;
            foreach (var dup in paths.Skip(1))
            {
                files.Add(new DiscoveredFile
                {
                    Path            = dup,
                    Name            = Path.GetFileName(dup),
                    SizeBytes       = size,
                    LastModified    = DateTimeOffset.Now,
                    RelevanceReason = $"Possible duplicate ({FormatBytes(size)})",
                });
            }
        }

        return (files.Take(100).ToList(), []);
    }

    private static async Task<(List<DiscoveredFile>, List<DiscoveredFolder>)>
        DiscoverScreenshotsAsync(string path, CancellationToken ct)
    {
        await Task.Yield();
        var screenshotExts = new HashSet<string> { ".png", ".jpg", ".jpeg", ".bmp" };
        var files = new List<DiscoveredFile>();

        foreach (var filePath in SafeEnumerateFiles(path, SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (!screenshotExts.Contains(ext)) continue;

            try
            {
                var info = new FileInfo(filePath);
                files.Add(new DiscoveredFile
                {
                    Path            = filePath,
                    Name            = info.Name,
                    SizeBytes       = info.Length,
                    LastModified    = new DateTimeOffset(info.LastWriteTime),
                    Category        = "Screenshot",
                    RelevanceReason = info.LastWriteTime.ToString("MMMM yyyy"),
                });
            }
            catch { }
        }

        return (files.OrderByDescending(f => f.LastModified).ToList(), []);
    }

    private static async Task<(List<DiscoveredFile>, List<DiscoveredFolder>)>
        DiscoverMediaAsync(string path, CancellationToken ct)
    {
        await Task.Yield();
        var mediaExts = new HashSet<string>
        {
            ".jpg", ".jpeg", ".png", ".heic",
            ".mp4", ".mov", ".avi", ".mkv",
            ".mp3", ".flac", ".aac", ".wav",
        };

        var files = new List<DiscoveredFile>();

        foreach (var filePath in SafeEnumerateFiles(path, SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (!mediaExts.Contains(ext)) continue;

            try
            {
                var info = new FileInfo(filePath);
                files.Add(new DiscoveredFile
                {
                    Path         = filePath,
                    Name         = info.Name,
                    SizeBytes    = info.Length,
                    LastModified = new DateTimeOffset(info.LastWriteTime),
                    Category     = GetCategory(ext),
                });
            }
            catch { }
        }

        return (files.Take(200).ToList(), []);
    }

    private static async Task<(List<DiscoveredFile>, List<DiscoveredFolder>)>
        DiscoverGeneralAsync(string path, CancellationToken ct)
    {
        await Task.Yield();
        var files = new List<DiscoveredFile>();

        foreach (var filePath in SafeEnumerateFiles(path, SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(filePath);
                files.Add(new DiscoveredFile
                {
                    Path         = filePath,
                    Name         = info.Name,
                    SizeBytes    = info.Length,
                    LastModified = new DateTimeOffset(info.LastWriteTime),
                    Category     = GetCategory(Path.GetExtension(filePath)),
                });
            }
            catch { }
        }

        return (files.Take(500).ToList(), []);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static FileDiscoveryResult EmptyResult(OperationalGoalType type, string reason) =>
        new()
        {
            Id               = FileDiscoveryResult.NewId(),
            GoalType         = type,
            Files            = [],
            Folders          = [],
            SummaryNarration = reason,
            DiscoveredAt     = DateTimeOffset.Now,
        };

    private static IEnumerable<string> SafeEnumerateFiles(
        string path, SearchOption option = SearchOption.TopDirectoryOnly)
    {
        try { return Directory.EnumerateFiles(path, "*", option); }
        catch { return []; }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(
        string path, SearchOption option = SearchOption.TopDirectoryOnly)
    {
        try { return Directory.EnumerateDirectories(path, "*", option); }
        catch { return []; }
    }

    private static (long totalBytes, int fileCount) MeasureFolder(string path)
    {
        long total = 0; int count = 0;
        foreach (var f in SafeEnumerateFiles(path, SearchOption.AllDirectories))
        {
            try { total += new FileInfo(f).Length; count++; } catch { }
        }
        return (total, count);
    }

    private static string GetCategory(string ext) => ext.ToLowerInvariant() switch
    {
        ".exe" or ".msi" or ".msix"                          => "Installer",
        ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx"
            or ".pptx" or ".txt" or ".csv"                   => "Document",
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp"
            or ".heic" or ".webp"                            => "Image",
        ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv"      => "Video",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz"        => "Archive",
        ".mp3" or ".flac" or ".wav" or ".aac" or ".ogg"     => "Audio",
        _ => "Other",
    };

    private static string BuildNarration(OperationalGoalType type, int fileCount, int folderCount) =>
        type switch
        {
            OperationalGoalType.LargeFileDiscovery  =>
                fileCount == 0 ? "No large files found." :
                $"Found {fileCount} file{(fileCount != 1 ? "s" : "")} over 100 MB.",
            OperationalGoalType.FolderSizeDiscovery =>
                folderCount == 0 ? "No folders found." :
                $"Measured {folderCount} folder{(folderCount != 1 ? "s" : "")}.",
            OperationalGoalType.ProjectDiscovery =>
                folderCount == 0 ? "No project folders found." :
                $"Found {folderCount} potential project folder{(folderCount != 1 ? "s" : "")}.",
            _ =>
                fileCount == 0 && folderCount == 0 ? "No matching files found." :
                $"Found {fileCount + folderCount} item{(fileCount + folderCount != 1 ? "s" : "")}.",
        };

    private static string FormatBytes(long b) => b switch
    {
        >= 1_073_741_824 => $"{b / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{b / 1_048_576.0:F1} MB",
        >= 1_024         => $"{b / 1_024.0:F0} KB",
        _                => $"{b} B",
    };
}
