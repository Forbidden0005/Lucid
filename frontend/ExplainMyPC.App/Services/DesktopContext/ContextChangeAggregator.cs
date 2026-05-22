namespace ExplainMyPC.Services.DesktopContext;

/// <summary>
/// Detects meaningful transitions between desktop context snapshots.
/// Suppresses noise (duplicate states, rapid oscillation, insignificant changes).
///
/// A change is "meaningful" if the app focus, explorer path, or clipboard type changed.
/// Minor title changes within the same app category are suppressed.
/// </summary>
internal sealed class ContextChangeAggregator
{
    private WorkflowContextSnapshot? _last;

    /// <summary>
    /// Returns a ContextChangedEventArgs if the new snapshot represents a
    /// meaningful change from the previous one; otherwise returns null.
    /// </summary>
    public ContextChangedEventArgs? Evaluate(WorkflowContextSnapshot current)
    {
        var previous = _last;
        _last = current;

        if (previous is null) return null;          // first snapshot — no baseline yet
        if (!IsMeaningfulChange(previous, current)) return null;

        return new ContextChangedEventArgs
        {
            Previous = previous,
            Current  = current,
        };
    }

    private static bool IsMeaningfulChange(
        WorkflowContextSnapshot prev,
        WorkflowContextSnapshot curr)
    {
        // App category changed
        if (prev.ActiveWindow?.AppCategory != curr.ActiveWindow?.AppCategory)
            return true;

        // Process changed within same category (e.g., notepad → code)
        if (!string.Equals(
                prev.ActiveWindow?.ProcessName,
                curr.ActiveWindow?.ProcessName,
                StringComparison.OrdinalIgnoreCase))
            return true;

        // Explorer path changed
        if (!string.Equals(
                prev.ExplorerContext?.CurrentPath,
                curr.ExplorerContext?.CurrentPath,
                StringComparison.OrdinalIgnoreCase))
            return true;

        // Clipboard type changed (text ↔ files ↔ empty)
        var prevClip = ClipboardSignature(prev.ClipboardContext);
        var currClip = ClipboardSignature(curr.ClipboardContext);
        if (prevClip != currClip) return true;

        // Operational focus label changed
        if (!string.Equals(
                prev.CurrentOperationalFocus,
                curr.CurrentOperationalFocus,
                StringComparison.Ordinal))
            return true;

        return false;
    }

    private static string ClipboardSignature(ClipboardContext? ctx)
    {
        if (ctx is null) return "none";
        if (ctx.HasFiles) return $"files:{ctx.FileCount}";
        if (ctx.HasText)  return "text";
        return "empty";
    }
}
