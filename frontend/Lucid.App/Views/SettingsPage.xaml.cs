using Lucid.Services.Automation;
using Lucid.Services.Governance;
using Lucid.Services.LlmChat;
using Lucid.Services.Settings;
using Lucid.Services.Trust;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;

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
    // Guard flag: prevents toggle Toggled handlers from firing saves while we
    // are programmatically initializing their IsOn state from persisted settings.
    private bool _initializing;

    // Registry key for Windows startup entries.
    private const string AutoStartKeyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValueName = "Lucid";

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _initializing = true;

        // ── General toggles ────────────────────────────────────────────────────
        var s = AppServices.Settings.Current;
        AutoStartToggle.IsOn        = ReadAutoStartFromRegistry();
        AutoScanToggle.IsOn         = s.AutoScanEnabled;
        UsageTelemetryToggle.IsOn   = s.UsageTelemetryEnabled;

        // ── Operational Trust combos ───────────────────────────────────────────
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

        var currentAuto = AppServices.AutomationOrchestrator.Mode;
        AutomationModeCombo.SelectedIndex = currentAuto switch
        {
            AutomationMode.ConfirmBeforeAction => 0,
            AutomationMode.SemiAutomatic       => 1,
            AutomationMode.GuidedAssist        => 2,
            AutomationMode.ObserveOnly         => 3,
            _                                  => 0,
        };

        // ── AI Assistant fields ────────────────────────────────────────────────
        LlmEndpointBox.Text = s.LlmEndpointUrl;
        LlmModelBox.Text    = s.LlmModel;

        _initializing = false;

        // ── Live telemetry rate ────────────────────────────────────────────────
        // Seed from current governance state, then subscribe for mode changes.
        RefreshTelemetryRateText();
        AppServices.Governance.ModeChanged += OnGovernanceModeChanged;

        AppServices.TrustManager.PostureChanged += OnTrustPostureChanged;

        Unloaded += (_, _) =>
        {
            AppServices.Governance.ModeChanged    -= OnGovernanceModeChanged;
            AppServices.TrustManager.PostureChanged -= OnTrustPostureChanged;
        };

        RefreshTrustPostureCaption();
    }

    // ── Telemetry rate ────────────────────────────────────────────────────────

    private void OnGovernanceModeChanged(object? sender, RuntimeModeChangedEventArgs e)
    {
        // ModeChanged is already raised on the UI thread by RuntimeGovernanceService.
        RefreshTelemetryRateText();
    }

    private void RefreshTelemetryRateText()
    {
        var interval = AppServices.Governance.GetSnapshot().TelemetryInterval;
        var mode     = AppServices.Governance.CurrentMode;

        var rateText = $"~{interval.TotalSeconds:0.#} s";
        var modeTag  = mode switch
        {
            RuntimeMode.HighLoad          => " · reducing (high CPU load)",
            RuntimeMode.LowPower          => " · reducing (battery mode)",
            RuntimeMode.Gaming            => " · reducing (game detected)",
            RuntimeMode.ThermalProtection => " · thermal protection active",
            _                             => "",
        };

        TelemetryRateText.Text = rateText + modeTag;
    }

    // ── General toggles ───────────────────────────────────────────────────────

    private void AutoStartToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        ApplyAutoStart(AutoStartToggle.IsOn);
    }

    private void AutoScanToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _ = AppServices.Settings.SaveAsync(
            AppServices.Settings.Current with { AutoScanEnabled = AutoScanToggle.IsOn });
    }

    private void UsageTelemetryToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _ = AppServices.Settings.SaveAsync(
            AppServices.Settings.Current with { UsageTelemetryEnabled = UsageTelemetryToggle.IsOn });
    }

    // ── Auto-start helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Reads the Windows startup registry key to determine the current auto-start state.
    /// The registry is the single source of truth — not persisted in AppSettings.
    /// </summary>
    private static bool ReadAutoStartFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartKeyPath, writable: false);
            return key?.GetValue(AutoStartValueName) is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Adds or removes the Lucid startup entry from the Windows run key.
    /// Uses the current process executable path so packaged and unpackaged
    /// deployments are handled correctly without hardcoding a path.
    /// Best-effort — silently ignores registry access failures.
    /// </summary>
    private static void ApplyAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartKeyPath, writable: true);
            if (key is null) return;

            if (enable)
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess()
                                  .MainModule?.FileName ?? string.Empty;
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(AutoStartValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AutoStartValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Registry access is best-effort. The toggle visual state may not
            // match the registry if access is denied, but we never crash.
        }
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

    // ── AI Assistant ─────────────────────────────────────────────────────────

    private void LlmEndpointBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var url = LlmEndpointBox.Text.Trim();
        if (string.IsNullOrEmpty(url)) url = OllamaClient.DefaultBaseUrl;

        var model = LlmModelBox.Text.Trim();
        if (string.IsNullOrEmpty(model)) model = OllamaClient.DefaultModelName;

        _ = AppServices.Settings.SaveAsync(
            AppServices.Settings.Current with { LlmEndpointUrl = url, LlmModel = model });

        // Reconfigure the live service — changes take effect on the next message,
        // no restart required. ReconfigureAsync waits for any in-flight stream to
        // finish cleanly before swapping the client.
        _ = AppServices.LlmChat.ReconfigureAsync(url, model);
    }

    private void LlmModelBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var model = LlmModelBox.Text.Trim();
        if (string.IsNullOrEmpty(model)) model = OllamaClient.DefaultModelName;

        var url = LlmEndpointBox.Text.Trim();
        if (string.IsNullOrEmpty(url)) url = OllamaClient.DefaultBaseUrl;

        _ = AppServices.Settings.SaveAsync(
            AppServices.Settings.Current with { LlmEndpointUrl = url, LlmModel = model });

        // Reconfigure the live service — changes take effect on the next message,
        // no restart required.
        _ = AppServices.LlmChat.ReconfigureAsync(url, model);
    }

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        TestConnectionButton.IsEnabled = false;
        LlmStatusText.Text = "Testing…";

        var url   = LlmEndpointBox.Text.Trim();
        var model = LlmModelBox.Text.Trim();

        try
        {
            using var client = new OllamaClient(url, model);

            if (!await client.IsAvailableAsync())
            {
                LlmStatusText.Text = "Ollama not reachable at this address";
                return;
            }

            var modelReady = await client.IsModelReadyAsync();
            LlmStatusText.Text = modelReady
                ? $"Connected — {model} is ready"
                : $"Ollama reachable, but '{model}' is not pulled";
        }
        catch (Exception ex)
        {
            LlmStatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
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
