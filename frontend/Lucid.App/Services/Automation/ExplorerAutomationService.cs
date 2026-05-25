using System.Diagnostics;

namespace Lucid.Services.Automation;

/// <summary>
/// Provides File Explorer navigation operations.
///
/// This service ONLY opens folders and reveals files — it NEVER moves, renames,
/// copies, or deletes files automatically. Every file-system operation must be
/// explicitly performed by the user.
///
/// Supported operations:
///   • Open a specific folder path in Explorer
///   • Reveal a file (open its parent folder, select the file)
///   • Open well-known folders (Downloads, Documents, Temp, Desktop)
///   • Generate an organization preview (read-only analysis — no moves)
///
/// All methods return (bool success, string? errorMessage).
/// Failures are non-fatal — the orchestrator logs them and surfaces the error
/// in narration without crashing the plan.
/// </summary>
public sealed class ExplorerAutomationService
{
    // ── Well-known folder openers ─────────────────────────────────────────────

    /// <summary>Opens the current user's Downloads folder in File Explorer.</summary>
    public async Task<(bool Success, string? Error)> OpenDownloadsFolderAsync()
        => await OpenFolderAsync(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads");

    /// <summary>Opens the current user's Documents folder in File Explorer.</summary>
    public async Task<(bool Success, string? Error)> OpenDocumentsFolderAsync()
        => await OpenFolderAsync(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

    /// <summary>Opens the current user's Desktop folder in File Explorer.</summary>
    public async Task<(bool Success, string? Error)> OpenDesktopFolderAsync()
        => await OpenFolderAsync(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop));

    /// <summary>
    /// Opens the Windows temp folder in File Explorer.
    /// Used as context when guiding users through manual cleanup.
    /// </summary>
    public async Task<(bool Success, string? Error)> OpenTempFolderAsync()
        => await OpenFolderAsync(Path.GetTempPath());

    /// <summary>Opens the Windows Recycle Bin folder in File Explorer.</summary>
    public async Task<(bool Success, string? Error)> OpenRecycleBinAsync()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = "explorer.exe",
                Arguments       = "shell:RecycleBinFolder",
                UseShellExecute = true,
            });
            await Task.Delay(200);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Could not open Recycle Bin: {ex.Message}");
        }
    }

    // ── Generic folder/file openers ───────────────────────────────────────────

    /// <summary>
    /// Opens the given folder path in File Explorer.
    /// If the path does not exist, returns (false, error message).
    /// </summary>
    public async Task<(bool Success, string? Error)> OpenFolderAsync(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return (false, $"Folder not found: {folderPath}");

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = "explorer.exe",
                Arguments       = $"\"{folderPath}\"",
                UseShellExecute = true,
            });
            await Task.Delay(200);  // brief yield so window has time to appear
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Could not open folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Reveals a specific file in File Explorer (opens its parent folder
    /// and selects the file). If the file does not exist, opens its parent folder.
    /// </summary>
    public async Task<(bool Success, string? Error)> RevealFileAsync(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName        = "explorer.exe",
                    Arguments       = $"/select,\"{filePath}\"",
                    UseShellExecute = true,
                });
            }
            else
            {
                var parent = Path.GetDirectoryName(filePath);
                if (parent is not null && Directory.Exists(parent))
                    return await OpenFolderAsync(parent);

                return (false, $"File not found: {filePath}");
            }

            await Task.Delay(200);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Could not reveal file: {ex.Message}");
        }
    }

    // ── Organization preview (read-only) ─────────────────────────────────────

    /// <summary>
    /// Scans a folder and returns a read-only preview showing what categories
    /// of files are present and how much space each takes.
    ///
    /// This is purely informational — NO files are moved, deleted, or renamed.
    /// The user reviews the preview and decides what to do.
    ///
    /// Scanning is bounded: top-level files only, max 500 files, 2-second timeout.
    /// </summary>
    public OrganizationPreview PreviewOrganization(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            return new OrganizationPreview
            {
                FolderPath  = folderPath,
                TotalFiles  = 0,
                TotalBytes  = 0,
                Categories  = [],
            };
        }

        try
        {
            var files = Directory.EnumerateFiles(folderPath)
                                  .Take(500)
                                  .Select(f => new FileInfo(f))
                                  .Where(fi => fi.Exists)
                                  .ToList();

            var groups = files
                .GroupBy(fi => ClassifyExtension(fi.Extension.ToLowerInvariant()))
                .Select(g => new OrganizationCategory
                {
                    CategoryName      = g.Key,
                    FileCount         = g.Count(),
                    TotalBytes        = g.Sum(fi => fi.Length),
                    ExampleExtensions = string.Join(", ",
                        g.Select(fi => fi.Extension.ToLowerInvariant())
                         .Distinct()
                         .Take(4)),
                })
                .OrderByDescending(c => c.TotalBytes)
                .ToList();

            return new OrganizationPreview
            {
                FolderPath  = folderPath,
                TotalFiles  = files.Count,
                TotalBytes  = files.Sum(fi => fi.Length),
                Categories  = groups,
            };
        }
        catch
        {
            return new OrganizationPreview
            {
                FolderPath  = folderPath,
                TotalFiles  = 0,
                TotalBytes  = 0,
                Categories  = [],
            };
        }
    }

    // ── Extension classifier ──────────────────────────────────────────────────

    private static string ClassifyExtension(string ext) => ext switch
    {
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".heic" or ".tiff"
            => "Images",
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".webm"
            => "Videos",
        ".mp3" or ".flac" or ".aac" or ".wav" or ".ogg" or ".m4a"
            => "Audio",
        ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".odt"
            => "Documents",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2"
            => "Archives",
        ".exe" or ".msi" or ".msix" or ".appx"
            => "Installers",
        ".cs" or ".py" or ".js" or ".ts" or ".html" or ".css" or ".json" or ".xml" or ".yml" or ".yaml"
            => "Code",
        _ => "Other",
    };
}
