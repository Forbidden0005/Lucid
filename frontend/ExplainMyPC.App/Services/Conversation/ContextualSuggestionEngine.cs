using ExplainMyPC.Services.Companion;
using ExplainMyPC.Services.DesktopContext;

namespace ExplainMyPC.Services.Conversation;

/// <summary>
/// Derives lightweight, non-intrusive suggestions from the current desktop context.
///
/// Uses the <see cref="IDesktopContextService.CurrentSnapshot"/> (already polled at
/// 1750ms) to surface relevant suggestions when the user appears to be doing
/// something the companion can assist with.
///
/// Design constraints:
///   • Stateless — synchronous, reads cached snapshot only; never triggers a poll.
///   • Subtle — suggestions are optional chips, never dialogs or alerts.
///   • Bounded — max 3 suggestions at a time to avoid overwhelming the UI.
///   • Honest — every suggestion explains WHY it was made (observable basis).
///   • Privacy — reads only what <see cref="IDesktopContextService"/> already
///     exposes; no additional observation beyond what Phase 17B collects.
///   • Non-manipulative — no fake urgency, no fabricated problem framing.
/// </summary>
public sealed class ContextualSuggestionEngine
{
    private readonly IDesktopContextService _desktopContext;

    public ContextualSuggestionEngine(IDesktopContextService desktopContext)
    {
        _desktopContext = desktopContext;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns up to 3 contextual suggestions based on the current desktop context.
    /// Returns an empty list when no context is available or no suggestions apply.
    /// </summary>
    public IReadOnlyList<ContextualSuggestion> GetSuggestions()
    {
        var snap = _desktopContext.CurrentSnapshot;
        if (snap is null) return [];

        var suggestions = new List<ContextualSuggestion>();

        // ── Explorer context suggestions ────────────────────────────────────
        var explorer = snap.ExplorerContext;
        if (explorer is not null)
        {
            if (explorer.IsDownloadsFolder)
            {
                suggestions.Add(new ContextualSuggestion
                {
                    Id        = "ctx-downloads-cleanup",
                    Text      = "Clean up Downloads",
                    Glyph     = "",   // Recycle bin
                    Target    = NavigationTarget.Storage,
                    QueryStub = "What can I safely clean from my downloads folder?",
                });
            }
            else if (explorer.IsMediaFolder)
            {
                suggestions.Add(new ContextualSuggestion
                {
                    Id        = "ctx-media-storage",
                    Text      = "Analyze media storage",
                    Glyph     = "",   // Photo/video
                    Target    = NavigationTarget.Storage,
                    QueryStub = "What's using the most storage space?",
                });
            }
            else if (explorer.IsDocumentsFolder)
            {
                suggestions.Add(new ContextualSuggestion
                {
                    Id        = "ctx-docs-duplicates",
                    Text      = "Check for duplicate files",
                    Glyph     = "",   // Documents
                    Target    = NavigationTarget.Storage,
                    QueryStub = "Are there any duplicate files I could remove?",
                });
            }
            else if (explorer.IsProjectFolder)
            {
                // Developer working in a project folder — suppress performance warnings silently,
                // offer process context instead
                suggestions.Add(new ContextualSuggestion
                {
                    Id        = "ctx-project-processes",
                    Text      = "View build processes",
                    Glyph     = "",   // Terminal
                    Target    = NavigationTarget.Processes,
                    QueryStub = "What processes are consuming resources right now?",
                });
            }
        }

        // ── Clipboard context suggestions ───────────────────────────────────
        var clipboard = snap.ClipboardContext;
        if (clipboard is not null && clipboard.HasFiles && clipboard.FileCount > 3)
        {
            suggestions.Add(new ContextualSuggestion
            {
                Id        = "ctx-clipboard-files",
                Text      = $"Review {clipboard.FileCount} clipboard files",
                Glyph     = "",   // Clipboard
                Target    = NavigationTarget.Storage,
                QueryStub = "Help me with the files I just copied.",
            });
        }

        // ── App category suggestions ────────────────────────────────────────
        if (snap.ActiveWindow is not null)
        {
            switch (snap.ActiveWindow.AppCategory)
            {
                case AppCategory.Browser:
                    // User is browsing — if memory pressure exists, offer context
                    // (we don't read browser content — only that a browser is active)
                    if (suggestions.Count < 2)
                    {
                        suggestions.Add(new ContextualSuggestion
                        {
                            Id        = "ctx-browser-memory",
                            Text      = "Check memory usage",
                            Glyph     = "",   // Brain/memory
                            Target    = NavigationTarget.Processes,
                            QueryStub = "What's using my RAM right now?",
                        });
                    }
                    break;

                case AppCategory.Terminal:
                    // Developer in a terminal — offer process overview
                    if (suggestions.Count < 2)
                    {
                        suggestions.Add(new ContextualSuggestion
                        {
                            Id        = "ctx-terminal-processes",
                            Text      = "View active processes",
                            Glyph     = "",   // Terminal glyph
                            Target    = NavigationTarget.Processes,
                            QueryStub = "What processes are running right now?",
                        });
                    }
                    break;
            }
        }

        // ── Cap at 3 suggestions ────────────────────────────────────────────
        return suggestions.Take(3).ToList();
    }

    /// <summary>
    /// Converts a <see cref="ContextualSuggestion"/> into a <see cref="QuickAction"/>
    /// so it can be surfaced in the existing quick-actions strip.
    /// </summary>
    public static QuickAction ToQuickAction(ContextualSuggestion suggestion) => new()
    {
        Id          = suggestion.Id,
        Label       = suggestion.Text,
        Glyph       = suggestion.Glyph,
        Description = $"Navigate to {suggestion.Target}",
        Query       = suggestion.QueryStub ?? $"Tell me about: {suggestion.Text}",
        IsContextual = true,
    };
}
