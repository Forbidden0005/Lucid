using Lucid.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lucid.ViewModels;

/// <summary>
/// Display-layer wrapper for a SystemIssue produced by the Intelligence Engine.
///
/// Translates the data model (IssueSeverity enum, bool CanFix) into ready-to-bind
/// view properties (glyph strings, SolidColorBrush objects, Visibility values).
/// All values are computed once at construction and are immutable, so the
/// DataTemplate can use plain x:Bind with no converters.
///
/// Brushes are pre-allocated as static readonly fields — zero heap allocation
/// per telemetry tick even when the issue list is rebuilt.
/// </summary>
public sealed class SystemIssueViewModel
{
    // ── Static brush palette — one allocation per severity level, ever ────────

    private static readonly SolidColorBrush s_criticalFore = Mk(240,  80,  80);
    private static readonly SolidColorBrush s_highFore     = Mk(240, 120,  60);
    private static readonly SolidColorBrush s_moderateFore = Mk(255, 179,  71);  // WarningColor
    private static readonly SolidColorBrush s_lowFore      = Mk( 87, 214, 141);  // SuccessColor
    private static readonly SolidColorBrush s_infoFore     = Mk( 78, 161, 255);  // AccentBlueColor

    private static readonly SolidColorBrush s_criticalBg   = Mk( 42,  10,  10);
    private static readonly SolidColorBrush s_highBg       = Mk( 42,  14,   8);
    private static readonly SolidColorBrush s_moderateBg   = Mk( 42,  30,  10);
    private static readonly SolidColorBrush s_lowBg        = Mk( 15,  30,  20);
    private static readonly SolidColorBrush s_infoBg       = Mk( 10,  20,  35);

    private static SolidColorBrush Mk(byte r, byte g, byte b) =>
        new(Color.FromArgb(255, r, g, b));

    // ── Display properties — all bound by the DataTemplate ───────────────────

    public string Title             { get; }
    public string Description       { get; }
    public string Category          { get; }

    /// <summary>Segoe MDL2 glyph character that represents the severity level.</summary>
    public string IconGlyph         { get; }

    /// <summary>Icon container and badge chip background tint per severity.</summary>
    public SolidColorBrush IconBackground  { get; }

    /// <summary>Icon and badge text foreground colour per severity.</summary>
    public SolidColorBrush IconForeground  { get; }

    /// <summary>Human-readable severity label shown in the badge chip.</summary>
    public string SeverityLabel     { get; }

    /// <summary>Collapses the "View Fix" button when no repair flow is wired up.</summary>
    public Visibility FixVisibility { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public SystemIssueViewModel(SystemIssue issue)
    {
        Title       = issue.Title;
        Description = issue.Description;
        Category    = issue.Category;

        IconGlyph        = ToGlyph(issue.Severity);
        IconForeground   = ToForeground(issue.Severity);
        IconBackground   = ToBackground(issue.Severity);
        SeverityLabel    = ToLabel(issue.Severity);
        FixVisibility    = issue.CanFix ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Severity → visual mapping ─────────────────────────────────────────────

    private static string ToGlyph(IssueSeverity s) => s switch
    {
        IssueSeverity.Critical  => "",  // ErrorBadge
        IssueSeverity.High      => "",  // Warning (filled)
        IssueSeverity.Moderate  => "",  // ReportHacked / caution
        IssueSeverity.Low       => "",  // Info
        _                       => "",
    };

    private static SolidColorBrush ToForeground(IssueSeverity s) => s switch
    {
        IssueSeverity.Critical  => s_criticalFore,
        IssueSeverity.High      => s_highFore,
        IssueSeverity.Moderate  => s_moderateFore,
        IssueSeverity.Low       => s_lowFore,
        _                       => s_infoFore,
    };

    private static SolidColorBrush ToBackground(IssueSeverity s) => s switch
    {
        IssueSeverity.Critical  => s_criticalBg,
        IssueSeverity.High      => s_highBg,
        IssueSeverity.Moderate  => s_moderateBg,
        IssueSeverity.Low       => s_lowBg,
        _                       => s_infoBg,
    };

    private static string ToLabel(IssueSeverity s) => s switch
    {
        IssueSeverity.Critical  => "Critical",
        IssueSeverity.High      => "High",
        IssueSeverity.Moderate  => "Moderate",
        IssueSeverity.Low       => "Low",
        _                       => "Info",
    };
}
