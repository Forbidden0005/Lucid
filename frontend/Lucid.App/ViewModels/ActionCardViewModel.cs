using Lucid.Services.Intelligence;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lucid.ViewModels;

/// <summary>
/// Display-layer wrapper for a <see cref="SystemAction"/> recommended by an
/// intelligence rule.
///
/// Translates ActionImpact / ActionEffort / ActionRisk enums into ready-to-bind
/// view properties — brushes, labels, Visibility tokens — so the DataTemplate
/// requires no converters.
///
/// Immutable after construction: actions are authored as static readonly fields
/// on each rule class and never change, so no INotifyPropertyChanged is needed.
///
/// Static brush palette:
///   One SolidColorBrush per impact/risk level, allocated at class init time.
///   The same brush instance is shared by every card of the same level.
/// </summary>
public sealed class ActionCardViewModel
{
    // ── Static brush palette ── one allocation per level, ever ────────────────

    // Impact accent colours (full opacity)
    private static readonly SolidColorBrush s_impactLow      = Mk(255,  78, 161, 255); // Blue  #4EA1FF
    private static readonly SolidColorBrush s_impactModerate = Mk(255,  85, 214, 255); // Cyan  #55D6FF
    private static readonly SolidColorBrush s_impactHigh     = Mk(255,  87, 214, 141); // Green #57D68D

    // Impact background tints (38 % alpha)
    private static readonly SolidColorBrush s_impactLowBg      = Mk(0x26,  78, 161, 255);
    private static readonly SolidColorBrush s_impactModerateBg = Mk(0x26,  85, 214, 255);
    private static readonly SolidColorBrush s_impactHighBg     = Mk(0x26,  87, 214, 141);

    // Risk accent colours (full opacity)
    private static readonly SolidColorBrush s_riskSafe     = Mk(255,  87, 214, 141); // Green  #57D68D
    private static readonly SolidColorBrush s_riskLow      = Mk(255,  78, 161, 255); // Blue   #4EA1FF
    private static readonly SolidColorBrush s_riskModerate = Mk(255, 255, 179,  71); // Orange #FFB347
    private static readonly SolidColorBrush s_riskHigh     = Mk(255, 255, 107, 107); // Red    #FF6B6B

    // Risk background tints (38 % alpha)
    private static readonly SolidColorBrush s_riskSafeBg     = Mk(0x26,  87, 214, 141);
    private static readonly SolidColorBrush s_riskLowBg      = Mk(0x26,  78, 161, 255);
    private static readonly SolidColorBrush s_riskModerateBg = Mk(0x26, 255, 179,  71);
    private static readonly SolidColorBrush s_riskHighBg     = Mk(0x26, 255, 107, 107);

    private static SolidColorBrush Mk(byte a, byte r, byte g, byte b) =>
        new(Color.FromArgb(a, r, g, b));

    // ── Backing model ─────────────────────────────────────────────────────────

    private readonly SystemAction _action;

    public ActionCardViewModel(SystemAction action)
    {
        _action   = action;
        Execution = new ActionExecutionViewModel(
            actionId:             action.Id,
            actionTitle:          action.Title,
            requiresConfirmation: action.RequiresConfirmation,
            confirmationMessage:  action.ConfirmationMessage);
    }

    // ── Execution sub-ViewModel ───────────────────────────────────────────────

    /// <summary>
    /// Drives the execution UX for this action — confirmation, live log,
    /// dry-run preview, result display, and rollback.
    ///
    /// Created once at construction and stable for the card's lifetime.
    /// Observable internally; the XAML binds through this property to reach
    /// <see cref="ActionExecutionViewModel"/>'s reactive surface.
    /// </summary>
    public ActionExecutionViewModel Execution { get; }

    // ── Raw model pass-through ────────────────────────────────────────────────

    public string  Id                   => _action.Id;
    public string  Title                => _action.Title;
    public string  Description          => _action.Description;
    public bool    IsReversible         => _action.IsReversible;
    public bool    RequiresConfirmation => _action.RequiresConfirmation;
    public string? ConfirmationMessage  => _action.ConfirmationMessage;

    // ── Effort ────────────────────────────────────────────────────────────────

    /// <summary>Human-readable effort estimate, e.g. "2–5 min".</summary>
    public string EffortText => _action.Effort switch
    {
        ActionEffort.Seconds    => "< 1 min",
        ActionEffort.FewMinutes => "2–5 min",
        ActionEffort.TenMinutes => "~10 min",
        _                       => "—",
    };

    // ── Impact visuals ────────────────────────────────────────────────────────

    /// <summary>Short impact label for the chip, e.g. "High impact".</summary>
    public string ImpactLabel => _action.Impact switch
    {
        ActionImpact.Low         => "Low impact",
        ActionImpact.Moderate    => "Moderate impact",
        ActionImpact.High        => "High impact",
        ActionImpact.Significant => "Significant impact",
        _                        => "Impact",
    };

    public SolidColorBrush ImpactBrush      => ToImpactAccent(_action.Impact);
    public SolidColorBrush ImpactBackground => ToImpactBg(_action.Impact);

    // ── Risk visuals ──────────────────────────────────────────────────────────

    /// <summary>Short risk label for the badge, e.g. "Low risk".</summary>
    public string RiskLabel => _action.Risk switch
    {
        ActionRisk.Safe     => "Safe",
        ActionRisk.Low      => "Low risk",
        ActionRisk.Moderate => "Moderate risk",
        ActionRisk.High     => "Higher risk",
        _                   => "Risk",
    };

    public SolidColorBrush RiskBrush      => ToRiskAccent(_action.Risk);
    public SolidColorBrush RiskBackground => ToRiskBg(_action.Risk);

    // ── Visibility tokens ─────────────────────────────────────────────────────

    /// <summary>Collapses the "Reversible" chip when the action is not reversible.</summary>
    public Visibility ReversibleVisibility =>
        IsReversible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Collapses the confirmation warning chip when none is needed.</summary>
    public Visibility ConfirmationVisibility =>
        RequiresConfirmation ? Visibility.Visible : Visibility.Collapsed;

    // ── Severity → visual mapping ─────────────────────────────────────────────

    private static SolidColorBrush ToImpactAccent(ActionImpact i) => i switch
    {
        ActionImpact.Low      => s_impactLow,
        ActionImpact.Moderate => s_impactModerate,
        _                     => s_impactHigh, // High + Significant share green
    };

    private static SolidColorBrush ToImpactBg(ActionImpact i) => i switch
    {
        ActionImpact.Low      => s_impactLowBg,
        ActionImpact.Moderate => s_impactModerateBg,
        _                     => s_impactHighBg,
    };

    private static SolidColorBrush ToRiskAccent(ActionRisk r) => r switch
    {
        ActionRisk.Safe     => s_riskSafe,
        ActionRisk.Low      => s_riskLow,
        ActionRisk.Moderate => s_riskModerate,
        ActionRisk.High     => s_riskHigh,
        _                   => s_riskSafe,
    };

    private static SolidColorBrush ToRiskBg(ActionRisk r) => r switch
    {
        ActionRisk.Safe     => s_riskSafeBg,
        ActionRisk.Low      => s_riskLowBg,
        ActionRisk.Moderate => s_riskModerateBg,
        ActionRisk.High     => s_riskHighBg,
        _                   => s_riskSafeBg,
    };
}
