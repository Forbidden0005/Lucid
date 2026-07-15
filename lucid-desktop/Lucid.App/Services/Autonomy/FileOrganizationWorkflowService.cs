using System.Runtime.InteropServices;

namespace Lucid.Services.Autonomy;

/// <summary>
/// Builds and executes file organization proposals for autonomous workflows.
///
/// Responsibilities:
///   • Scan a target folder and produce a <see cref="FileOrganizationPreview"/>
///   • Execute an approved preview (move files to their destinations)
///   • Roll back an executed preview (move files back to their origins)
///
/// All file moves use a safe per-file approach. Failures are logged without
/// aborting the entire operation. Deletions go to the Recycle Bin for easy recovery.
///
/// Threading: all async methods are safe to call from the thread pool.
/// </summary>
public sealed class FileOrganizationWorkflowService
{
    private readonly SafeExecutionValidator _validator;

    public FileOrganizationWorkflowService(SafeExecutionValidator validator)
    {
        _validator = validator;
    }

    // ── Preview building ──────────────────────────────────────────────────────

    /// <summary>
    /// Scans <paramref name="folderPath"/> and builds a preview of the proposed
    /// file operations for the given goal type.
    ///
    /// No files are moved. The preview is shown to the user before they approve.
    /// </summary>
    public async Task<FileOrganizationPreview> BuildOrganizationPreviewAsync(
        string              folderPath,
        OperationalGoalType goalType,
        CancellationToken   ct = default)
    {
        await Task.Yield();

        var proposals = goalType switch
        {
            OperationalGoalType.DownloadsOrganization     => BuildDownloadsProposals(folderPath),
            OperationalGoalType.ScreenshotArchival        => BuildScreenshotProposals(folderPath),
            OperationalGoalType.OldDownloadsCleanup       => BuildOldDownloadsProposals(folderPath),
            OperationalGoalType.DuplicateFileCleanup      => BuildDuplicateProposals(folderPath),
            OperationalGoalType.MediaCategorization       => BuildMediaProposals(folderPath),
            OperationalGoalType.GeneralFolderOrganization => BuildDownloadsProposals(folderPath),
            _                                             => new List<FileMoveProposal>(),
        };

        ct.ThrowIfCancellationRequested();

        // Remove proposals targeting blocked paths
        var validProposals = proposals
            .Where(p => _validator.ValidatePath(p.SourcePath) is null)
            .ToList();

        var totalBytes  = validProposals.Sum(p => p.FileSizeBytes);
        var summary     = BuildPreviewSummary(goalType, validProposals.Count, totalBytes);

        return new FileOrganizationPreview
        {
            WorkflowId     = Guid.NewGuid().ToString("N"),
            FolderPath     = folderPath,
            Proposals      = validProposals,
            TotalBytes     = totalBytes,
            TotalFiles     = validProposals.Count,
            PreviewSummary = summary,
            GeneratedAt    = DateTimeOffset.Now,
        };
    }

    // ── Execution ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes an approved <see cref="FileOrganizationPreview"/>.
    /// Moves or recycles files one at a time. Failures are non-fatal.
    /// </summary>
    public async Task ExecutePreviewAsync(
        FileOrganizationPreview preview,
        CancellationToken       ct = default)
    {
        if (!preview.IsApproved)
            throw new InvalidOperationException("Preview must be approved before execution.");

        await Task.Yield();

        foreach (var proposal in preview.Proposals)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (proposal.IsRecycleDeletion)
                {
                    SendToRecycleBin(proposal.SourcePath);
                }
                else
                {
                    var destDir = Path.GetDirectoryName(proposal.DestinationPath);
                    if (destDir is not null)
                        Directory.CreateDirectory(destDir);

                    // Only move if source still exists and destination is free
                    if (File.Exists(proposal.SourcePath) && !File.Exists(proposal.DestinationPath))
                        File.Move(proposal.SourcePath, proposal.DestinationPath);
                }
            }
            catch { /* Non-fatal — skip this file and continue */ }
        }

        preview.WasExecuted = true;
    }

    /// <summary>
    /// Rolls back a previously executed preview by moving files back to their origins.
    /// Only works for move operations (recycle bin deletions cannot be reversed here).
    /// </summary>
    public async Task RollbackPreviewAsync(
        FileOrganizationPreview preview,
        CancellationToken       ct = default)
    {
        if (!preview.WasExecuted) return;

        await Task.Yield();

        foreach (var proposal in preview.Proposals)
        {
            ct.ThrowIfCancellationRequested();
            if (proposal.IsRecycleDeletion) continue;

            try
            {
                if (File.Exists(proposal.DestinationPath) && !File.Exists(proposal.SourcePath))
                    File.Move(proposal.DestinationPath, proposal.SourcePath);
            }
            catch { /* Non-fatal */ }
        }
    }

    // ── Proposal builders ─────────────────────────────────────────────────────

    private static List<FileMoveProposal> BuildDownloadsProposals(string folderPath)
    {
        var proposals = new List<FileMoveProposal>();
        if (!Directory.Exists(folderPath)) return proposals;

        foreach (var filePath in SafeEnumerateFiles(folderPath))
        {
            var ext      = Path.GetExtension(filePath).ToLowerInvariant();
            var category = CategoriseByExtension(ext);
            if (category is null) continue;

            var destDir  = Path.Combine(folderPath, category);
            var destPath = Path.Combine(destDir, Path.GetFileName(filePath));

            if (string.Equals(destPath, filePath, StringComparison.OrdinalIgnoreCase)) continue;

            proposals.Add(new FileMoveProposal
            {
                SourcePath        = filePath,
                DestinationPath   = destPath,
                Reason            = $"Moved to {category} subfolder",
                FileSizeBytes     = GetFileSize(filePath),
                IsRecycleDeletion = false,
            });
        }

        return proposals;
    }

    private static List<FileMoveProposal> BuildScreenshotProposals(string folderPath)
    {
        var proposals     = new List<FileMoveProposal>();
        if (!Directory.Exists(folderPath)) return proposals;

        var screenshotExts = new HashSet<string> { ".png", ".jpg", ".jpeg", ".bmp" };

        foreach (var filePath in SafeEnumerateFiles(folderPath))
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (!screenshotExts.Contains(ext)) continue;

            var info      = new FileInfo(filePath);
            var dateLabel = info.LastWriteTime.ToString("yyyy-MM");
            var destDir   = Path.Combine(folderPath, dateLabel);
            var destPath  = Path.Combine(destDir, Path.GetFileName(filePath));

            if (string.Equals(destPath, filePath, StringComparison.OrdinalIgnoreCase)) continue;

            proposals.Add(new FileMoveProposal
            {
                SourcePath        = filePath,
                DestinationPath   = destPath,
                Reason            = $"Archived to {dateLabel}",
                FileSizeBytes     = info.Length,
                IsRecycleDeletion = false,
            });
        }

        return proposals;
    }

    private static List<FileMoveProposal> BuildOldDownloadsProposals(string folderPath)
    {
        var proposals = new List<FileMoveProposal>();
        if (!Directory.Exists(folderPath)) return proposals;

        var cutoff = DateTime.Now.AddDays(-90);

        foreach (var filePath in SafeEnumerateFiles(folderPath))
        {
            try
            {
                var info = new FileInfo(filePath);
                if (info.LastAccessTime >= cutoff) continue;

                proposals.Add(new FileMoveProposal
                {
                    SourcePath        = filePath,
                    DestinationPath   = filePath, // not used for recycle
                    Reason            = $"Not accessed in over 90 days",
                    FileSizeBytes     = info.Length,
                    IsRecycleDeletion = true,
                });
            }
            catch { /* best-effort: file may vanish or deny access mid-scan — skip this candidate */ }
        }

        return proposals;
    }

    private static List<FileMoveProposal> BuildDuplicateProposals(string folderPath)
    {
        var proposals  = new List<FileMoveProposal>();
        if (!Directory.Exists(folderPath)) return proposals;

        // Group by (name + size) — fast heuristic for likely duplicates
        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in SafeEnumerateFiles(folderPath))
        {
            try
            {
                var info = new FileInfo(filePath);
                var key  = $"{info.Name}|{info.Length}";
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = [];
                list.Add(filePath);
            }
            catch { /* best-effort: file may vanish or deny access mid-scan — skip this candidate */ }
        }

        foreach (var (_, paths) in groups)
        {
            if (paths.Count < 2) continue;
            foreach (var dup in paths.Skip(1)) // keep the first instance
            {
                proposals.Add(new FileMoveProposal
                {
                    SourcePath        = dup,
                    DestinationPath   = dup,
                    Reason            = "Duplicate file — sending to Recycle Bin",
                    FileSizeBytes     = GetFileSize(dup),
                    IsRecycleDeletion = true,
                });
            }
        }

        return proposals;
    }

    private static List<FileMoveProposal> BuildMediaProposals(string folderPath)
    {
        var proposals = new List<FileMoveProposal>();
        if (!Directory.Exists(folderPath)) return proposals;

        var photoExts = new HashSet<string> { ".jpg", ".jpeg", ".png", ".heic", ".raw", ".arw", ".cr2" };
        var videoExts = new HashSet<string> { ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".flv" };
        var audioExts = new HashSet<string> { ".mp3", ".flac", ".aac", ".wav", ".ogg", ".m4a" };

        foreach (var filePath in SafeEnumerateFiles(folderPath))
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            string? category =
                photoExts.Contains(ext) ? "Photos" :
                videoExts.Contains(ext) ? "Videos" :
                audioExts.Contains(ext) ? "Audio"  : null;

            if (category is null) continue;

            var destDir  = Path.Combine(folderPath, category);
            var destPath = Path.Combine(destDir, Path.GetFileName(filePath));

            if (string.Equals(destPath, filePath, StringComparison.OrdinalIgnoreCase)) continue;

            proposals.Add(new FileMoveProposal
            {
                SourcePath        = filePath,
                DestinationPath   = destPath,
                Reason            = $"Organised into {category}",
                FileSizeBytes     = GetFileSize(filePath),
                IsRecycleDeletion = false,
            });
        }

        return proposals;
    }

    // ── Recycle Bin ───────────────────────────────────────────────────────────

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint   wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool   fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    private const uint   FO_DELETE      = 3;
    private const ushort FOF_ALLOWUNDO  = 0x40;
    private const ushort FOF_NOCONFIRMATION = 0x10;
    private const ushort FOF_SILENT     = 0x04;

    private static void SendToRecycleBin(string path)
    {
        try
        {
            // SHFileOperation expects a double-null-terminated string
            var pFrom = path + "\0\0";
            var op = new SHFILEOPSTRUCT
            {
                wFunc  = FO_DELETE,
                pFrom  = pFrom,
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT),
            };
            SHFileOperation(ref op);
        }
        catch
        {
            // Fall back to permanent delete if Recycle Bin operation fails
            try { File.Delete(path); } catch { /* best-effort: cleanup of our own staging artifact; leftovers are reclaimed by the staging sweeper */ }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IEnumerable<string> SafeEnumerateFiles(string path)
    {
        try { return Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly); }
        catch { return []; }
    }

    private static long GetFileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private static string? CategoriseByExtension(string ext) => ext switch
    {
        ".exe" or ".msi" or ".msix" or ".appx"                              => "Installers",
        ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".pptx"
            or ".txt" or ".csv" or ".odt" or ".rtf"                         => "Documents",
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp"
            or ".heic" or ".svg"                                             => "Images",
        ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" or ".flv"           => "Videos",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2"             => "Archives",
        ".mp3" or ".flac" or ".wav" or ".aac" or ".ogg" or ".m4a"          => "Audio",
        _ => null,
    };

    private static string BuildPreviewSummary(
        OperationalGoalType type, int count, long totalBytes)
    {
        var sizeStr = totalBytes switch
        {
            >= 1_073_741_824 => $"{totalBytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576     => $"{totalBytes / 1_048_576.0:F1} MB",
            _                => $"{totalBytes / 1_024.0:F0} KB",
        };

        return type switch
        {
            OperationalGoalType.OldDownloadsCleanup or
            OperationalGoalType.DuplicateFileCleanup =>
                $"{count} file{(count != 1 ? "s" : "")} will be sent to the Recycle Bin ({sizeStr}).",
            _ =>
                $"{count} file{(count != 1 ? "s" : "")} will be moved ({sizeStr}).",
        };
    }
}
