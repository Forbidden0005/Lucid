using System.Runtime.InteropServices;
using System.Text;

namespace Lucid.Services.VisualContext;

/// <summary>
/// Interprets File Explorer windows to identify the folder type and suggest
/// relevant operational actions.
///
/// Approach:
///   1. Read the address bar via child window enumeration (class "ToolbarWindow32"
///      that contains the path text, or the top-level window title which Explorer
///      sets to the current folder name).
///   2. Match the path to known special folders via Environment.GetFolderPath.
///   3. Apply lightweight heuristics (path segments, folder names).
///
/// Privacy rules:
///   • Does NOT enumerate file names or read file content.
///   • Reads only the folder path (from window title / address bar).
///   • No content scanning without explicit user request.
/// </summary>
public sealed class ExplorerVisualInterpreter
{
    // ── Win32 P/Invokes ───────────────────────────────────────────────────────

    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Interprets the given Explorer HWND and returns an <see cref="ExplorerContext"/>.
    /// </summary>
    public ExplorerContext Interpret(IntPtr hwnd, string windowTitle)
    {
        var path = ExtractCurrentPath(hwnd, windowTitle);
        return BuildContext(path, windowTitle);
    }

    // ── Path extraction ───────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to extract the current folder path from the Explorer window.
    /// Strategy:
    ///   1. Walk child windows looking for an "Edit" control inside
    ///      the address bar ("Breadcrumb Parent" or "Address Band Root").
    ///   2. Fall back to the window title (which Explorer sets to the folder name).
    /// </summary>
    private static string? ExtractCurrentPath(IntPtr hwnd, string windowTitle)
    {
        string? found = null;

        EnumChildWindows(hwnd, (child, _) =>
        {
            if (found is not null) return false;

            var sb  = new StringBuilder(256);
            GetClassName(child, sb, sb.Capacity);
            var cls = sb.ToString();

            // The address bar Edit control lives inside "Address Band Root" → "Edit"
            if (!cls.Equals("Edit", StringComparison.OrdinalIgnoreCase)) return true;

            var textSb = new StringBuilder(512);
            GetWindowText(child, textSb, textSb.Capacity);
            var text = textSb.ToString().Trim();

            // Only accept if it looks like a file-system path or known URI
            if (text.Length > 2 && (text[1] == ':' || text.StartsWith("\\\\") ||
                                     text.StartsWith("http", StringComparison.OrdinalIgnoreCase)))
            {
                found = text;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return found ?? TitleToPath(windowTitle);
    }

    /// <summary>
    /// Extracts a likely path from the window title as a fallback.
    /// Explorer sets the title to the folder name (e.g. "Downloads", "Documents").
    /// </summary>
    private static string? TitleToPath(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        // Check if the title matches a known special folder name
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Downloads"]  = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                              + @"\Downloads",
            ["Documents"]  = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ["Pictures"]   = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            ["Videos"]     = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            ["Music"]      = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            ["Desktop"]    = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };

        return known.TryGetValue(title, out var p) ? p : null;
    }

    // ── Context construction ──────────────────────────────────────────────────

    private static ExplorerContext BuildContext(string? path, string windowTitle)
    {
        var viewType     = ClassifyPath(path, windowTitle);
        var description  = BuildDescription(viewType, path);
        var observations = BuildObservations(viewType, path);
        var suggestions  = BuildSuggestions(viewType);

        return new ExplorerContext
        {
            ViewType            = viewType,
            CurrentPath         = path,
            Description         = description,
            Observations        = observations,
            WorkflowSuggestions = suggestions,
        };
    }

    // ── View classification ───────────────────────────────────────────────────

    private static ExplorerViewType ClassifyPath(string? path, string windowTitle)
    {
        var testPath  = (path ?? string.Empty).ToLowerInvariant();
        var testTitle = windowTitle.ToLowerInvariant();

        // Special shell paths
        if (testPath.Contains("recycle") || testTitle.Contains("recycle bin"))
            return ExplorerViewType.RecycleBin;

        if (testPath.StartsWith("\\\\") || testTitle.Contains("network"))
            return ExplorerViewType.NetworkLocation;

        // OneDrive
        if (testPath.Contains("onedrive"))
            return ExplorerViewType.OneDrive;

        // Screenshots — OneDrive\Screenshots or Pictures\Screenshots
        if (testPath.Contains("screenshots") || testTitle.Contains("screenshots"))
            return ExplorerViewType.Screenshots;

        // Common special folders
        var profile  = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                                   .ToLowerInvariant();
        var downloads = Path.Combine(profile, "downloads");
        var docs     = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                                   .ToLowerInvariant();
        var pics     = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
                                   .ToLowerInvariant();
        var videos   = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
                                   .ToLowerInvariant();
        var music    = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
                                   .ToLowerInvariant();
        var desktop  = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                                   .ToLowerInvariant();
        var temp     = Path.GetTempPath().ToLowerInvariant().TrimEnd('\\');

        if (!string.IsNullOrEmpty(testPath))
        {
            if (testPath.StartsWith(downloads) || testTitle == "downloads")
                return ExplorerViewType.Downloads;
            if (testPath.StartsWith(desktop) || testTitle == "desktop")
                return ExplorerViewType.Desktop;
            if (testPath == profile || testPath == profile + "\\")
                return ExplorerViewType.UserProfile;
            if (testPath.StartsWith(pics) || testTitle == "pictures")
                return ExplorerViewType.Pictures;
            if (testPath.StartsWith(videos) || testTitle == "videos")
                return ExplorerViewType.Videos;
            if (testPath.StartsWith(music) || testTitle == "music")
                return ExplorerViewType.Music;
            if (testPath.StartsWith(docs) || testTitle == "documents")
                return ExplorerViewType.Documents;
            if (testPath.StartsWith(temp))
                return ExplorerViewType.Temp;

            // Drive root (e.g. "C:\")
            if (testPath.Length == 3 && testPath[1] == ':' && testPath[2] == '\\')
                return ExplorerViewType.DriveRoot;

            // Program Files
            if (testPath.Contains("program files"))
                return ExplorerViewType.ProgramFiles;
        }

        // Heuristic: does the folder name look like a project workspace?
        if (LooksLikeProjectFolder(testPath))
            return ExplorerViewType.ProjectWorkspace;

        return ExplorerViewType.GenericFolder;
    }

    private static bool LooksLikeProjectFolder(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var lower = path.ToLowerInvariant();
        return lower.Contains("\\repos\\") ||
               lower.Contains("\\projects\\") ||
               lower.Contains("\\source\\") ||
               lower.Contains("\\dev\\")    ||
               lower.Contains("\\code\\");
    }

    // ── Descriptions & observations ───────────────────────────────────────────

    private static string BuildDescription(ExplorerViewType type, string? path) => type switch
    {
        ExplorerViewType.Downloads        => "This is the Downloads folder — files downloaded from the internet are stored here.",
        ExplorerViewType.Desktop          => "This is the Desktop folder.",
        ExplorerViewType.Screenshots      => "This is a Screenshots folder — captured screen images are stored here.",
        ExplorerViewType.Pictures         => "This is the Pictures folder.",
        ExplorerViewType.Videos           => "This is the Videos folder.",
        ExplorerViewType.Music            => "This is the Music folder.",
        ExplorerViewType.Documents        => "This is the Documents folder.",
        ExplorerViewType.Temp             => "This is a Temporary files folder — these files can often be safely cleaned up.",
        ExplorerViewType.DriveRoot        => $"This is the root of a drive ({path ?? "unknown"}).",
        ExplorerViewType.ProgramFiles     => "This is the Program Files directory.",
        ExplorerViewType.RecycleBin       => "This is the Recycle Bin.",
        ExplorerViewType.UserProfile      => "This is the User Profile folder.",
        ExplorerViewType.OneDrive         => "This is a OneDrive synced folder.",
        ExplorerViewType.ProjectWorkspace => $"This looks like a project workspace folder ({Path.GetFileName(path ?? string.Empty)}).",
        ExplorerViewType.NetworkLocation  => "This appears to be a network location.",
        _                                 => $"File Explorer is showing: {Path.GetFileName(path ?? string.Empty)}",
    };

    private static IReadOnlyList<string> BuildObservations(ExplorerViewType type, string? path)
    {
        var obs = new List<string>();
        switch (type)
        {
            case ExplorerViewType.Downloads:
                obs.Add("Downloads folders commonly accumulate large installer files over time.");
                obs.Add("Archive files (.zip, .rar, .7z) here are often safe to remove after extraction.");
                break;
            case ExplorerViewType.Temp:
                obs.Add("Temporary files here are generally safe to remove.");
                obs.Add("Running Disk Cleanup or Storage Sense will clear this folder safely.");
                break;
            case ExplorerViewType.Screenshots:
                obs.Add("Screenshot files can accumulate and take significant disk space.");
                obs.Add("Consider reviewing and removing screenshots you no longer need.");
                break;
            case ExplorerViewType.RecycleBin:
                obs.Add("Items in the Recycle Bin are still taking disk space until emptied.");
                break;
            case ExplorerViewType.ProjectWorkspace:
                obs.Add("Project workspaces may contain build artefacts and dependency folders that take significant space.");
                break;
        }
        return obs;
    }

    private static IReadOnlyList<string> BuildSuggestions(ExplorerViewType type) => type switch
    {
        ExplorerViewType.Downloads    => ["Scan for large files", "Identify old downloads (> 90 days)", "Find duplicate files"],
        ExplorerViewType.Temp         => ["Clear temporary files", "Run Disk Cleanup"],
        ExplorerViewType.Screenshots  => ["Organise screenshots by date", "Find large screenshots"],
        ExplorerViewType.RecycleBin   => ["Empty Recycle Bin"],
        ExplorerViewType.Pictures     => ["Find large image files", "Identify duplicate photos"],
        ExplorerViewType.Videos       => ["Find large video files"],
        ExplorerViewType.ProjectWorkspace => ["Identify build artefact folders", "Find large files in workspace"],
        _                             => [],
    };
}
