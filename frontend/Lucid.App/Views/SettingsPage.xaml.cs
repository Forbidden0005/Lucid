using Lucid.Services.Automation;
using Lucid.Services.Settings;
using Lucid.Services.Trust;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lucid.Views;

/// <summary>
/// Settings page — app preferences, operational trust configuration.
///
/// Operational Trust section (Phase 17E):
///   • ConsentModeCombo   — sets AutomationConsentService mode
///   • AutomationModeCombo — sets AutomationOrchestrator mode
///   • TrustPostureCaption — shows current posture + session stats
///   • ResetTrustPostureButton — resets posture to Standard
/// </summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Sync combo box selection with current consent mode
        var currentConsent = AppServices.AutomationConsent.CurrentMode;
        ConsentModeCombo.SelectedIndex = currentConsent switch
        {
            TrustConsentMode.AskAlways               => 0,
            TrustConsentMode.AskForMediumAndHighRisk => 1,
            TrustConsentMode.AskHighRiskOnly         => 2,
            TrustConsentMode.GuidedOnly              => 3,
            TrustConsentMode.ObserveOnly             => 4,
            _                                        => 1,
        };

        // Sync automation mode combo
        var currentAuto = AppServices.AutomationOrchestrator.Mode;
        AutomationModeCombo.SelectedIndex = currentAuto switch
        {
            AutomationMode.ConfirmBeforeAction => 0,
            AutomationMode.SemiAutomatic       => 1,
            AutomationMode.GuidedAssist        => 2,
            AutomationMode.ObserveOnly         => 3,
            _                                  => 0,
        };

        // Subscribe to posture changes
        AppServices.TrustManager.PostureChanged += OnTrustPostureChanged;
        Unloaded += (_, _) => AppServices.TrustManager.PostureChanged -= OnTrustPostureChanged;

        RefreshTrustPostureCaption();
    }

    // ── Trust posture ─────────────────────────────────────────────────────────

    private void OnTrustPostureChanged(object? sender, TrustPosture posture)
    {
        DispatcherQueue.TryEnqueue(RefreshTrustPostureCaption);
    }

    private void RefreshTrustPostureCaption()
    {
        TrustPostureCaption.Text = AppServices.TrustManager.GetTrustProfile();
    }

    private void ResetTrustPostureButton_Click(object sender, RoutedEventArgs e)
    {
        AppServices.TrustManager.ResetPosture();
        RefreshTrustPostureCaption();
    }

    // ── Consent mode ──────────────────────────────────────────────────────────

    private void ConsentModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConsentModeCombo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            var mode = tag switch
            {
                "AskAlways"               => TrustConsentMode.AskAlways,
                "AskForMediumAndHighRisk" => TrustConsentMode.AskForMediumAndHighRisk,
                "AskHighRiskOnly"         => TrustConsentMode.AskHighRiskOnly,
                "GuidedOnly"              => TrustConsentMode.GuidedOnly,
                "ObserveOnly"             => TrustConsentMode.ObserveOnly,
                _                         => TrustConsentMode.AskForMediumAndHighRisk,
            };
            AppServices.AutomationConsent.SetMode(mode);

            // Persist inline — the tag string is the enum name, exactly what AppSettings stores.
            _ = AppServices.Settings.SaveAsync(
                AppServices.Settings.Current with { ConsentMode = tag });
        }
    }

    // ── Automation mode ───────────────────────────────────────────────────────

    private void AutomationModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AutomationModeCombo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            var mode = tag switch
            {
                "ConfirmBeforeAction" => AutomationMode.ConfirmBeforeAction,
                "SemiAutomatic"       => AutomationMode.SemiAutomatic,
                "GuidedAssist"        => AutomationMode.GuidedAssist,
                "ObserveOnly"         => AutomationMode.ObserveOnly,
                _                     => AutomationMode.ConfirmBeforeAction,
            };
            AppServices.AutomationOrchestrator.SetMode(mode);

            // Persist inline — the tag string is the enum name, exactly what AppSettings stores.
            _ = AppServices.Settings.SaveAsync(
                AppServices.Settings.Current with { AutomationMode = tag });
        }
    }
}
