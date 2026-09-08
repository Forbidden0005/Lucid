using System.Text;

namespace Lucid.Services.Chat;

/// <summary>
/// Derives a short, human-readable session title from the first thing the user said.
///
/// Deterministic and local — no model call, no network. The user's opening line is
/// almost always the best available description of what the conversation is about
/// ("my pc gets loud when I open chrome" → "My pc gets loud when I open chrome"),
/// and generating a title from it costs nothing and never surprises the user with
/// an invented name.
///
/// A generated title is always provisional: <see cref="ChatSessionSummary.IsAutoTitled"/>
/// stays true until the user renames the session, after which this is never applied again.
/// </summary>
public static class ChatSessionTitleGenerator
{
    /// <summary>Title used when there is nothing to derive one from.</summary>
    public const string DefaultTitle = "New conversation";

    /// <summary>Maximum rendered title length, including the ellipsis when truncated.</summary>
    public const int MaxLength = 48;

    /// <summary>
    /// Builds a title from the first user message. Returns <see cref="DefaultTitle"/>
    /// for null, empty, or whitespace-only input.
    /// </summary>
    public static string FromFirstMessage(string? message)
    {
        var text = CollapseWhitespace(message);
        if (text.Length == 0) return DefaultTitle;

        // Trailing sentence periods read as noise in a list; question marks carry
        // meaning ("Why is my disk full?") so they stay.
        text = text.TrimEnd('.', ',', ';', ':');
        if (text.Length == 0) return DefaultTitle;

        text = Truncate(text);

        // Capitalise the first letter so lowercase typing still yields a tidy rail.
        if (char.IsLower(text[0]))
            text = char.ToUpperInvariant(text[0]) + text[1..];

        return text;
    }

    /// <summary>
    /// Normalises a user-supplied title from the rename dialog. Falls back to
    /// <see cref="DefaultTitle"/> when the user submits nothing usable.
    /// </summary>
    public static string Sanitize(string? title)
    {
        var text = CollapseWhitespace(title);
        return text.Length == 0 ? DefaultTitle : Truncate(text);
    }

    /// <summary>
    /// Builds the rail's secondary line from a message body: single-line,
    /// whitespace-collapsed, capped at <paramref name="maxLength"/>.
    /// </summary>
    public static string BuildPreview(string? message, int maxLength = 90)
    {
        var text = CollapseWhitespace(message);
        return text.Length <= maxLength ? text : text[..(maxLength - 1)].TrimEnd() + "…";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Flattens newlines, tabs and runs of spaces into single spaces and trims.
    /// Pasted log output is a common first message — without this the rail would
    /// show a title full of line breaks.
    /// </summary>
    private static string CollapseWhitespace(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var sb        = new StringBuilder(input.Length);
        var lastSpace = false;

        foreach (var ch in input)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastSpace && sb.Length > 0) sb.Append(' ');
                lastSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastSpace = false;
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Caps the text at <see cref="MaxLength"/>, breaking on a word boundary when
    /// one is available in the last third of the budget so titles do not end
    /// mid-word.
    /// </summary>
    private static string Truncate(string text)
    {
        if (text.Length <= MaxLength) return text;

        var budget   = MaxLength - 1;               // room for the ellipsis
        var slice    = text[..budget];
        var lastSpace = slice.LastIndexOf(' ');

        if (lastSpace >= budget * 2 / 3)
            slice = slice[..lastSpace];

        return slice.TrimEnd() + "…";
    }
}
