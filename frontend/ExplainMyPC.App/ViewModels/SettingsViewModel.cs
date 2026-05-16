using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplainMyPC.Core.MVVM;

namespace ExplainMyPC.ViewModels;

/// <summary>
/// ViewModel for the Settings page.
///
/// Manages user preferences that persist to SQLite (Phase 1 uses defaults).
/// All settings are opt-in; nothing is changed without user action.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    // ── Appearance ───────────────────────────────────────────────────────────

    [ObservableProperty] private bool _darkModeEnabled = true;

    // ── Telemetry & Scanning ─────────────────────────────────────────────────

    [ObservableProperty] private bool _autoScanEnabled       = false;
    [ObservableProperty] private int  _autoScanIntervalDays  = 7;

    // ── Privacy ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Per security-model.md: telemetry upload is always opt-in and off by default.
    /// </summary>
    [ObservableProperty] private bool _usageTelemetryEnabled = false;

    // ── About ────────────────────────────────────────────────────────────────

    public string AppVersion => "0.1.0-alpha";
    public string AppDescription =>
        "ExplainMyPC helps you understand, diagnose, and safely maintain your Windows system.";

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        IsBusy        = true;
        StatusMessage = "Saving…";
        await Task.Delay(200); // Phase 2: persist to SQLite via SettingsRepository
        IsBusy        = false;
        StatusMessage = "Settings saved.";
    }
}
