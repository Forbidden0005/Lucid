using Lucid.Services.Execution;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lucid.ViewModels;

/// <summary>
/// Immutable display-layer wrapper for an <see cref="ActionLogEntry"/> produced
/// during action execution.
///
/// Translates <see cref="ActionLogLevel"/> into ready-to-bind brushes and glyphs
/// so the log DataTemplate requires no converters.
///
/// Static brush palette:
///   One brush per severity level, allocated at class init and shared across
///   every log row. Zero allocations per log entry.
/// </summary>
public sealed class ActionLogEntryViewModel
{
    // ── Static palette ── one allocation per level, ever ──────────────────────

    private static readonly SolidColorBrush s_infoBrush    = Mk(255,  78, 161, 255); // Blue   #4EA1FF
    private static readonly SolidColorBrush s_warningBrush = Mk(255, 255, 179,  71); // Orange #FFB347
    private static readonly SolidColorBrush s_errorBrush   = Mk(255, 255, 107, 107); // Red    #FF6B6B

    private static SolidColorBrush Mk(byte a, byte r, byte g, byte b) =>
        new(Color.FromArgb(a, r, g, b));

    // ── Construction ──────────────────────────────────────────────────────────

    public ActionLogEntryViewModel(ActionLogEntry entry)
    {
        Timestamp = entry.Timestamp.ToString("HH:mm:ss.ff");
        Message   = entry.Message;
        Level     = entry.Level;

        (LevelBrush, LevelGlyph, LevelLabel) = entry.Level switch
        {
            ActionLogLevel.Warning => (s_warningBrush, "", "WARN"),
            ActionLogLevel.Error   => (s_errorBrush,   "", "ERR "),
            _                      => (s_infoBrush,    "", "INFO"),
        };
    }

    // ── Bindable surface ──────────────────────────────────────────────────────

    /// <summary>Formatted timestamp, e.g. "14:02:37.08".</summary>
    public string          Timestamp  { get; }

    /// <summary>Full log message text.</summary>
    public string          Message    { get; }

    /// <summary>Severity level of this entry.</summary>
    public ActionLogLevel  Level      { get; }

    /// <summary>Severity-matched foreground brush for the glyph and message.</summary>
    public SolidColorBrush LevelBrush { get; }

    /// <summary>Segoe MDL2 Assets glyph for the severity level.</summary>
    public string          LevelGlyph { get; }

    /// <summary>Four-character severity abbreviation, e.g. "INFO", "WARN", "ERR ".</summary>
    public string          LevelLabel { get; }
}
