namespace Lucid.Services.DesktopContext;

/// <summary>
/// Builds a <see cref="WorkflowContextSnapshot"/> from the three provider outputs.
/// Generates probabilistic, evidence-grounded workflow hints.
///
/// Hints MUST remain:
///   • probabilistic ("appears to be" / "may be")
///   • evidence-grounded (inferred from observable signals only)
///   • never emotional/personal/identity inference
/// </summary>
internal static class WorkflowContextSnapshotBuilder
{
    public static WorkflowContextSnapshot Build(
        ActiveWindowContext? window,
        ExplorerContext?     explorer,
        ClipboardContext?    clipboard)
    {
        var focus = DeriveOperationalFocus(window, explorer);
        var hints = BuildHints(window, explorer, clipboard);

        return new WorkflowContextSnapshot
        {
            ActiveWindow            = window,
            ExplorerContext         = explorer,
            ClipboardContext        = clipboard,
            CurrentOperationalFocus = focus,
            DetectedWorkflowHints   = hints,
            SnapshotCreatedAt       = DateTimeOffset.Now,
        };
    }

    private static string? DeriveOperationalFocus(
        ActiveWindowContext? window,
        ExplorerContext?     explorer)
    {
        if (window is null) return null;

        return window.AppCategory switch
        {
            AppCategory.Explorer when explorer?.IsDownloadsFolder == true => "Browsing Downloads",
            AppCategory.Explorer when explorer?.IsProjectFolder   == true => "Browsing project files",
            AppCategory.Explorer when explorer?.IsMediaFolder     == true => "Browsing media files",
            AppCategory.Explorer when explorer?.IsDocumentsFolder == true => "Browsing documents",
            AppCategory.Explorer                                           => "File Explorer",
            AppCategory.Browser  => "Web browsing",
            AppCategory.Editor   => $"Working in {window.ProcessName}",
            AppCategory.Terminal => "Terminal / command line",
            AppCategory.Media    => "Media playback",
            AppCategory.Settings => "System settings",
            _                    => string.IsNullOrEmpty(window.WindowTitle)
                                        ? null : TruncateTitle(window.WindowTitle, 40),
        };
    }

    private static IReadOnlyList<string> BuildHints(
        ActiveWindowContext? window,
        ExplorerContext?     explorer,
        ClipboardContext?    clipboard)
    {
        var hints = new List<string>();

        // Explorer-based hints
        if (explorer is not null)
        {
            if (explorer.IsDownloadsFolder)
                hints.Add("User appears to be organizing downloads");
            if (explorer.IsMediaFolder)
                hints.Add("User is browsing large media folders");
            if (explorer.IsProjectFolder)
                hints.Add("User may be working on a development project");
            if (explorer.SelectedItems.Count > 5)
                hints.Add($"{explorer.SelectedItems.Count} items selected — possible batch file operation");
        }

        // Window-based hints
        if (window is not null)
        {
            if (window.AppCategory == AppCategory.Terminal)
                hints.Add("User may be troubleshooting or running system commands");
            if (window.AppCategory == AppCategory.Settings)
                hints.Add("User is reviewing system settings");
            if (window.AppCategory == AppCategory.Browser &&
                window.WindowTitle.Contains("download", StringComparison.OrdinalIgnoreCase))
                hints.Add("User appears to be downloading content");
        }

        // Clipboard-based hints
        if (clipboard is not null)
        {
            if (clipboard.HasFiles && clipboard.FileCount > 10)
                hints.Add($"Clipboard contains {clipboard.FileCount} files — possible file management activity");
            if (clipboard.HasFiles && clipboard.FileCount > 0 && clipboard.FileCount <= 10)
                hints.Add("Clipboard contains copied files");
        }

        return hints;
    }

    private static string TruncateTitle(string title, int max) =>
        title.Length <= max ? title : title[..max] + "…";
}
