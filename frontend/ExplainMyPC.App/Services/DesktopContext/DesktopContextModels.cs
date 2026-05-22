namespace ExplainMyPC.Services.DesktopContext;

public enum AppCategory { Explorer, Browser, Editor, Game, Terminal, Media, Settings, Unknown }

public sealed record ActiveWindowContext
{
    public string  ProcessName  { get; init; } = string.Empty;
    public string  WindowTitle  { get; init; } = string.Empty;
    public AppCategory AppCategory { get; init; }
    public DateTimeOffset DetectedAt { get; init; }
}

public sealed record ExplorerContext
{
    public string?                CurrentPath    { get; init; }
    public IReadOnlyList<string>  SelectedItems  { get; init; } = [];
    public bool                   IsDownloadsFolder { get; init; }
    public bool                   IsProjectFolder   { get; init; }
    public bool                   IsMediaFolder     { get; init; }
    public bool                   IsDocumentsFolder { get; init; }
}

public sealed record ClipboardContext
{
    public bool          HasText    { get; init; }
    public bool          HasFiles   { get; init; }
    public string?       TextPreview { get; init; }  // truncated, max 80 chars
    public int           FileCount  { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record WorkflowContextSnapshot
{
    public ActiveWindowContext?   ActiveWindow     { get; init; }
    public ExplorerContext?       ExplorerContext   { get; init; }
    public ClipboardContext?      ClipboardContext  { get; init; }
    public string?                CurrentOperationalFocus { get; init; }
    public IReadOnlyList<string>  DetectedWorkflowHints   { get; init; } = [];
    public DateTimeOffset         SnapshotCreatedAt        { get; init; }
}

public sealed class ContextChangedEventArgs : EventArgs
{
    public WorkflowContextSnapshot Previous { get; init; } = new() { SnapshotCreatedAt = DateTimeOffset.MinValue };
    public WorkflowContextSnapshot Current  { get; init; } = new() { SnapshotCreatedAt = DateTimeOffset.MinValue };
}
