using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Automation;
using Lucid.Services.DesktopContext;
using Lucid.Services.Governance;
using Lucid.Services.LlmChat;
using Lucid.Services.Settings;
using Lucid.Services.Trust;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Win32;

namespace Lucid.ViewModels;

/// <summary>
/// ViewModel for the Settings page.
///
/// Owns all observable state previously scattered across SettingsPage code-behind:
///   • General toggles (AutoStart, AutoScan, UsageTelemetry)
///   • Live telemetry rate display (adaptive, from RuntimeGovernanceService)
///   • Operational trust configuration (consent mode, automation mode, trust posture)
///   • AI assistant endpoint and model configuration
///
/// Threading:
///   Initialize() must be called on the UI thread (reads DispatcherQueue).
///   PostureChanged fires on a background thread — dispatched via _dispatcher.
///   ModeChanged fires on the UI thread — no dispatch needed.
///
/// Initialization guard:
///   _isInitializing prevents On{Prop}Changed partial handlers from triggering
///   saves or live service calls while we load initial values from persisted state.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    // ── Services ──────────────────────────────────────────────────────────────

    private readonly ISettingsService          _settings;
    private readonly IRuntimeGovernanceService _governance;
    private readonly OperationalTrustManager   _trustManager;
    private readonly AutomationConsentService  _automationConsent;
    private readonly AutomationOrchestrator    _automationOrchestrator;
    private readonly ILlmChatService           _llmChat;
    private readonly IDesktopContextService    _desktopContext;
    private readonly DispatcherQueue           _dispatcher;

    private bool _isInitializing;

    // ── Registry constants ────────────────────────────────────────────────────

    private const string AutoStartKeyPath   = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValueName = "Lucid";

    // ── General section ───────────────────────────────────────────────────────

    [ObservableProperty] private bool _autoStartEnabled;
    [ObservableProperty] private bool _autoScanEnabled;
    [ObservableProperty] private bool _usageTelemetryEnabled;
    [ObservableProperty] private bool _desktopContextEnabled;

    // ── Governance section ────────────────────────────────────────────────────

    [ObservableProperty] private string _telemetryRateText = "~1 s";

    // ── Trust section ─────────────────────────────────────────────────────────

    [ObservableProperty] private string _trustPostureCaption = "Loading…";
    [ObservableProperty] private int    _consentModeIndex    = 1;
    [ObservableProperty] private int    _automationModeIndex = 0;

    // ── AI Assistant section ──────────────────────────────────────────────────

    [ObservableProperty] private string _llmEndpointUrl          = OllamaClient.DefaultBaseUrl;
    [ObservableProperty] private string _llmModel                = OllamaClient.DefaultModelName;
    [ObservableProperty] private string _llmStatusText           = "Not tested";
    [ObservableProperty] private bool   _isTestConnectionEnabled = true;

    // ── Construction ──────────────────────────────────────────────────────────

    public SettingsViewModel(
        ISettingsService          settings,
        IRuntimeGovernanceService governance,
        OperationalTrustManager   trustManager,
        AutomationConsentService  automationConsent,
        AutomationOrchestrator    automationOrchestrator,
        ILlmChatService           llmChat,
        IDesktopContextService    desktopContext)
    {
        _settings               = settings;
        _governance             = governance;
        _trustManager           = trustManager;
        _automationConsent      = automationConsent;
        _automationOrchestrator = automationOrchestrator;
        _llmChat                = llmChat;
        _desktopContext         = desktopContext;
        _dispatcher             = DispatcherQueue.GetForCurrentThread()
                                  ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads initial values from persisted settings and live services.
    /// Must be called on the UI thread from the page Loaded handler.
    /// </summary>
    public void Initialize()
    {
        _isInitializing = true;

        var s = _settings.Current;
        AutoStartEnabled      = ReadAutoStartFromRegistry();
        AutoScanEnabled       = s.AutoScanEnabled;
        UsageTelemetryEnabled = s.UsageTelemetryEnabled;
        DesktopContextEnabled = s.DesktopContextAwarenessEnabled;

        ConsentModeIndex = _automationConsent.CurrentMode switch
        {
            TrustConsentMode.AskAlways               => 0,
            TrustConsentMode.AskForMediumAndHighRisk => 1,
            TrustConsentMode.AskHighRiskOnly         => 2,
            TrustConsentMode.GuidedOnly              => 3,
            TrustConsentMode.ObserveOnly             => 4,
            _                                        => 1,
        };

        AutomationModeIndex = _automationOrchestrator.Mode switch
        {
            AutomationMode.ConfirmBeforeAction => 0,
            AutomationMode.SemiAutomatic       => 1,
            AutomationMode.GuidedAssist        => 2,
            AutomationMode.ObserveOnly         => 3,
            _                                  => 0,
        };

        LlmEndpointUrl = s.LlmEndpointUrl;
        LlmModel       = s.LlmModel;

        _isInitializing = false;

        RefreshTelemetryRateText();
        RefreshTrustPostureCaption();

        _governance.ModeChanged      += OnGovernanceModeChanged;
        _trustManager.PostureChanged += OnTrustPostureChanged;
    }

    /// <summary>Unsubscribes from upstream events. Call from page Unloaded.</summary>
    public void Cleanup()
    {
        _governance.ModeChanged      -= OnGovernanceModeChanged;
        _trustManager.PostureChanged -= OnTrustPostureChanged;
    }

    // ── Property change handlers ──────────────────────────────────────────────

    partial void OnAutoStartEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        ApplyAutoStart(value);
    }

    partial void OnAutoScanEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        SaveSettings(_settings.Current with { AutoScanEnabled = value });
    }

    partial void OnUsageTelemetryEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        SaveSettings(_settings.Current with { UsageTelemetryEnabled = value });
    }

    partial void OnDesktopContextEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        SaveSettings(_settings.Current with { DesktopContextAwarenessEnabled = value });

        // Apply immediately — consent changes take effect without a restart.
        // Both calls are safe on the UI thread (this handler runs there) and
        // Start()/Stop() are idempotent.
        if (value) _desktopContext.Start();
        else       _desktopContext.Stop();
    }

    partial void OnConsentModeIndexChanged(int value)
    {
        if (_isInitializing) return;

        var (mode, tag) = value switch
        {
            0 => (TrustConsentMode.AskAlways,               "AskAlways"),
            1 => (TrustConsentMode.AskForMediumAndHighRisk, "AskForMediumAndHighRisk"),
            2 => (TrustConsentMode.AskHighRiskOnly,         "AskHighRiskOnly"),
            3 => (TrustConsentMode.GuidedOnly,              "GuidedOnly"),
            4 => (TrustConsentMode.ObserveOnly,             "ObserveOnly"),
            _ => (TrustConsentMode.AskForMediumAndHighRisk, "AskForMediumAndHighRisk"),
        };

        _automationConsent.SetMode(mode);
        SaveSettings(_settings.Current with { ConsentMode = tag });
    }

    partial void OnAutomationModeIndexChanged(int value)
    {
        if (_isInitializing) return;

        var (mode, tag) = value switch
        {
            0 => (AutomationMode.ConfirmBeforeAction, "ConfirmBeforeAction"),
            1 => (AutomationMode.SemiAutomatic,       "SemiAutomatic"),
            2 => (AutomationMode.GuidedAssist,        "GuidedAssist"),
            3 => (AutomationMode.ObserveOnly,         "ObserveOnly"),
            _ => (AutomationMode.ConfirmBeforeAction, "ConfirmBeforeAction"),
        };

        _automationOrchestrator.SetMode(mode);
        SaveSettings(_settings.Current with { AutomationMode = tag });
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ResetTrustPosture()
    {
        _trustManager.ResetPosture();
        RefreshTrustPostureCaption();
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsTestConnectionEnabled = false;
        LlmStatusText           = "Testing…";

        try
        {
            var url   = string.IsNullOrWhiteSpace(LlmEndpointUrl)
                ? OllamaClient.DefaultBaseUrl   : LlmEndpointUrl.Trim();
            var model = string.IsNullOrWhiteSpace(LlmModel)
                ? OllamaClient.DefaultModelName : LlmModel.Trim();

            using var client = new OllamaClient(url, model);

            if (!await client.IsAvailableAsync())
            {
                LlmStatusText = "Ollama not reachable at this address";
                return;
            }

            var modelReady = await client.IsModelReadyAsync();
            LlmStatusText  = modelReady
                ? $"Connected — {model} is ready"
                : $"Ollama reachable, but '{model}' is not pulled";
        }
        catch (Exception ex)
        {
            LlmStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsTestConnectionEnabled = true;
        }
    }

    // ── LLM settings apply (called from XAML LostFocus binding) ──────────────

    /// <summary>
    /// Validates, saves, and applies LLM endpoint/model settings.
    /// Bound to LostFocus on both TextBox controls via x:Bind method reference.
    /// </summary>
    public void OnLlmFieldLostFocus(object sender, RoutedEventArgs e)
    {
        var url = string.IsNullOrWhiteSpace(LlmEndpointUrl)
            ? OllamaClient.DefaultBaseUrl   : LlmEndpointUrl.Trim();
        var model = string.IsNullOrWhiteSpace(LlmModel)
            ? OllamaClient.DefaultModelName : LlmModel.Trim();

        // Normalize (may update text in box if user left it blank)
        LlmEndpointUrl = url;
        LlmModel       = model;

        SaveSettings(_settings.Current with { LlmEndpointUrl = url, LlmModel = model });
        ReconfigureLlm(url, model);
    }

    // ── Upstream event handlers ───────────────────────────────────────────────

    private void OnGovernanceModeChanged(object? sender, RuntimeModeChangedEventArgs e)
        => RefreshTelemetryRateText();   // ModeChanged already fires on the UI thread

    private void OnTrustPostureChanged(object? sender, TrustPosture posture)
        => _dispatcher.TryEnqueue(RefreshTrustPostureCaption);

    // ── Private helpers ───────────────────────────────────────────────────────

    private void RefreshTelemetryRateText()
    {
        var interval = _governance.GetSnapshot().TelemetryInterval;
        var mode     = _governance.CurrentMode;

        var modeTag = mode switch
        {
            RuntimeMode.HighLoad          => " · reducing (high CPU load)",
            RuntimeMode.LowPower          => " · reducing (battery mode)",
            RuntimeMode.Gaming            => " · reducing (game detected)",
            RuntimeMode.ThermalProtection => " · thermal protection active",
            _                             => "",
        };

        TelemetryRateText = $"~{interval.TotalSeconds:0.#} s{modeTag}";
    }

    private void RefreshTrustPostureCaption()
        => TrustPostureCaption = _trustManager.GetTrustProfile();

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
            // Best-effort — registry access may be denied in some environments.
        }
    }

    private async void SaveSettings(AppSettings settings)
    {
        try
        {
            await _settings.SaveAsync(settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsVM] SaveAsync failed: {ex}");
        }
    }

    private async void ReconfigureLlm(string url, string model)
    {
        try
        {
            await _llmChat.ReconfigureAsync(url, model);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsVM] LlmChat.ReconfigureAsync failed: {ex}");
        }
    }
}
