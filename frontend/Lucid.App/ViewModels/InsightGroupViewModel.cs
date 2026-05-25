using Lucid.Services.Intelligence;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lucid.ViewModels;

/// <summary>
/// A severity-tier group of <see cref="InsightCardViewModel"/> instances for
/// display in the dashboard's Active Insights section.
///
/// Groups are not individually observable — the outer <see cref="DashboardViewModel"/>
/// replaces the entire <c>InsightGroups</c> list whenever the engine publishes
/// a new finding set, so the outer ItemsRepeater binding drives all updates.
///
/// Only non-empty groups are exposed by the view model, so
/// <see cref="GroupVisibility"/> is always <see cref="Visibility.Visible"/> in
/// practice — it is kept for defensive bindings.
/// </summary>
public sealed class InsightGroupViewModel
{
    // ── Static brushes — one allocation per severity tier ─────────────────────

    private static readonly SolidColorBrush s_warningAccent = Mk(255, 255, 179,  71);
    private static readonly SolidColorBrush s_infoAccent    = Mk(255,  78, 161, 255);
    private static readonly SolidColorBrush s_goodAccent    = Mk(255,  87, 214, 141);

    private static readonly SolidColorBrush s_warningTint   = Mk(0x26, 255, 179,  71);
    private static readonly SolidColorBrush s_infoTint      = Mk(0x26,  78, 161, 255);
    private static readonly SolidColorBrush s_goodTint      = Mk(0x26,  87, 214, 141);

    private static SolidColorBrush Mk(byte a, byte r, byte g, byte b) =>
        new(Color.FromArgb(a, r, g, b));

    // ── Properties ────────────────────────────────────────────────────────────

    public InsightSeverity                   Severity         { get; }

    /// <summary>Localised header label, e.g. "Warnings".</summary>
    public string                            GroupTitle       { get; }

    /// <summary>Segoe MDL2 glyph for the group header icon.</summary>
    public string                            GroupIcon        { get; }

    /// <summary>Cards within this group, already sorted by priority.</summary>
    public IReadOnlyList<InsightCardViewModel> Cards          { get; }

    /// <summary>Solid accent colour for the header icon and count badge.</summary>
    public SolidColorBrush                   GroupAccentBrush { get; }

    /// <summary>Translucent tint for the count badge background.</summary>
    public SolidColorBrush                   GroupTintBrush   { get; }

    // ── Derived display tokens ─────────────────────────────────────────────────

    public string    CountText      => $"{Cards.Count} finding{(Cards.Count == 1 ? "" : "s")}";
    public bool      HasItems       => Cards.Count > 0;
    public Visibility GroupVisibility => HasItems ? Visibility.Visible : Visibility.Collapsed;

    // ── Constructor ───────────────────────────────────────────────────────────

    public InsightGroupViewModel(
        InsightSeverity                      severity,
        string                               groupTitle,
        string                               groupIcon,
        IReadOnlyList<InsightCardViewModel>  cards)
    {
        Severity         = severity;
        GroupTitle       = groupTitle;
        GroupIcon        = groupIcon;
        Cards            = cards;
        GroupAccentBrush = ToAccent(severity);
        GroupTintBrush   = ToTint(severity);
    }

    // ── Severity → colour mapping ─────────────────────────────────────────────

    private static SolidColorBrush ToAccent(InsightSeverity s) => s switch
    {
        InsightSeverity.Warning        => s_warningAccent,
        InsightSeverity.Recommendation => s_infoAccent,
        _                              => s_goodAccent,
    };

    private static SolidColorBrush ToTint(InsightSeverity s) => s switch
    {
        InsightSeverity.Warning        => s_warningTint,
        InsightSeverity.Recommendation => s_infoTint,
        _                              => s_goodTint,
    };
}
