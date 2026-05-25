using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Core.MVVM;
using Lucid.Services.Settings;

namespace Lucid.ViewModels;

/// <summary>
/// ViewModel for the Settings page.
///
/// Loads persisted preferences from <see cref="AppServices.Settings"/> at
/// construction time and writes them back on <see cref="SaveSettingsCommand"/>.
///
/// Operational trust settings (consent mode, automation mode) are handled
/// separately by SettingsPage.xaml.cs — those combo boxes write to the service
/// directly and persist inline rather than going through this ViewModel.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    // ── Appearance ───────────────────────────────────────────────────────────

    [ObservableProperty] private bool _darkModeEnabled;

    // ── Telemetry & Scanning ─────────────────────────────────────────────────

    [ObservableProperty] private bool _autoScanEnabled;
    [ObservableProperty] private int  _autoScanIntervalDays;

    // ── Privacy ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Per security-model.md: telemetry upload is always opt-in and off by default.
    /// </summary>
    [ObservableProperty] private bool _usageTelemetryEnabled;

    // ── About ────────────────────────────────────────────────────────────────

    public string AppVersion => "0.1.0-alpha";
    public string AppDescription =>
        "Lucid helps you understand, diagnose, and safely maintain your Windows system.";

    // ── Construction ─────────────────────────────────────────────────────────

    public SettingsViewModel()
    {
        var saved = AppServices.Settings.Current;
        _darkModeEnabled       = saved.DarkModeEnabled;
        _autoScanEnabled       = saved.AutoScanEnabled;
        _autoScanIntervalDays  = saved.AutoScanIntervalDays;
        _usageTelemetryEnabled = saved.UsageTelemetryEnabled;
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        IsBusy        = true;
        StatusMessage = "Saving…";

        // Build a new record, preserving operational trust settings that are
        // managed independently by SettingsPage.xaml.cs.
        var current = AppServices.Settings.Current;
        await AppServices.Settings.SaveAsync(new AppSettings
        {
            DarkModeEnabled       = DarkModeEnabled,
            AutoScanEnabled       = AutoScanEnabled,
            AutoScanIntervalDays  = AutoScanIntervalDays,
            UsageTelemetryEnabled = UsageTelemetryEnabled,
            ConsentMode           = current.ConsentMode,
            AutomationMode        = current.AutomationMode,
        });

        IsBusy        = false;
        StatusMessage = "Settings saved.";
    }
}
