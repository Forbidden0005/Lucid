using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Apps;
using Lucid.Services.Startup;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lucid.ViewModels;

/// <summary>
/// Display-layer ViewModel for a single installed application entry.
///
/// Wraps an <see cref="InstalledAppRecord"/> and exposes:
///   • Formatted display strings (install date, size, version)
///   • Startup impact badge label and colour brush
///   • <see cref="ToggleStartupCommand"/> — enables or disables the startup entry
///
/// IsStartupEnabled is toggled optimistically (UI updates immediately) then
/// the registry write commits on a background thread.  On failure the flag
/// is rolled back to its previous value.
/// </summary>
public sealed partial class AppEntryViewModel : ObservableObject
{
    private readonly InstalledAppRecord _record;

    // ── Display strings ───────────────────────────────────────────────────────

    public string Name      => _record.Name;
    public string Publisher => _record.Publisher;
    public string Version   => _record.Version;

    public string InstallDateText => _record.InstallDate.HasValue
        ? _record.InstallDate.Value.ToString("MMM d, yyyy")
        : "";

    public string SizeText => _record.EstimatedSizeKb switch
    {
        <= 0    => "",
        < 1_024 => $"{_record.EstimatedSizeKb:N0} KB",
        _       => $"{_record.EstimatedSizeKb / 1024.0:F0} MB",
    };

    // ── Startup state ─────────────────────────────────────────────────────────

    public bool HasStartup => _record.LinkedStartupEntry is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleStartupLabel))]
    [NotifyPropertyChangedFor(nameof(ToggleStartupGlyph))]
    private bool _isStartupEnabled;

    public string StartupImpactLabel => _record.LinkedStartupEntry?.Impact switch
    {
        StartupImpact.High     => "High impact",
        StartupImpact.Moderate => "Moderate impact",
        StartupImpact.Low      => "Low impact",
        _                      => "Unknown impact",
    };

    /// <summary>Hex colour for the startup impact badge.</summary>
    public string StartupImpactColor => _record.LinkedStartupEntry?.Impact switch
    {
        StartupImpact.High     => "#EF4444",
        StartupImpact.Moderate => "#F59E0B",
        StartupImpact.Low      => "#22C55E",
        _                      => "#6B7280",
    };

    /// <summary>Brush for the startup impact badge (bindable in WinUI DataTemplates).</summary>
    public SolidColorBrush StartupImpactBrush => _record.LinkedStartupEntry?.Impact switch
    {
        StartupImpact.High     => new SolidColorBrush(Color.FromArgb(255, 0xEF, 0x44, 0x44)),
        StartupImpact.Moderate => new SolidColorBrush(Color.FromArgb(255, 0xF5, 0x9E, 0x0B)),
        StartupImpact.Low      => new SolidColorBrush(Color.FromArgb(255, 0x22, 0xC5, 0x5E)),
        _                      => new SolidColorBrush(Color.FromArgb(255, 0x6B, 0x72, 0x80)),
    };

    public string StartupLocationText => _record.LinkedStartupEntry?.Location switch
    {
        StartupLocation.HklmRun       => "All users",
        StartupLocation.HkcuRun       => "Current user",
        StartupLocation.StartupFolder => "Startup folder",
        _                             => "",
    };

    public string ToggleStartupLabel => IsStartupEnabled ? "Disable" : "Enable";

    /// <summary>Segoe MDL2 glyph: blocked circle when enabled, play arrow when disabled.</summary>
    public string ToggleStartupGlyph => IsStartupEnabled ? "" : ""; // Blocked / Play

    // ── Uninstall ─────────────────────────────────────────────────────────────

    /// <summary>True when the app registers an interactive uninstall command.</summary>
    public bool CanUninstall => !string.IsNullOrWhiteSpace(_record.UninstallCommand);

    /// <summary>
    /// Launches the app's own uninstaller (the registered UninstallString —
    /// the same command Windows Settings runs). Removal is confirmed and
    /// carried out inside that uninstaller's UI, never silently by Lucid.
    /// Returns false when no command is registered or the launch failed.
    /// </summary>
    public bool LaunchUninstaller()
    {
        if (!CanUninstall) return false;

        try
        {
            var (file, args) = SplitCommandLine(_record.UninstallCommand);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = file,
                Arguments       = args,
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Splits a registry command line into file + arguments: a leading quoted
    /// segment wins; otherwise split after the first ".exe"; otherwise treat
    /// the whole string as the file (covers bare paths without arguments).
    /// </summary>
    private static (string File, string Args) SplitCommandLine(string command)
    {
        command = command.Trim();

        if (command.StartsWith('"'))
        {
            int close = command.IndexOf('"', 1);
            if (close > 0)
                return (command[1..close], command[(close + 1)..].Trim());
        }

        int exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0)
        {
            int end = exe + 4;
            return (command[..end], command[end..].Trim());
        }

        return (command, "");
    }

    // ── Visibility helpers ────────────────────────────────────────────────────

    public Visibility UninstallVisibility  => CanUninstall                           ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StartupVisibility    => HasStartup                             ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PublisherVisibility  => !string.IsNullOrEmpty(Publisher)       ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VersionVisibility    => !string.IsNullOrEmpty(Version)         ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SizeVisibility       => !string.IsNullOrEmpty(SizeText)        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DateVisibility       => !string.IsNullOrEmpty(InstallDateText) ? Visibility.Visible : Visibility.Collapsed;

    // ── Construction ──────────────────────────────────────────────────────────

    public AppEntryViewModel(InstalledAppRecord record)
    {
        _record = record;

        if (record.LinkedStartupEntry is { } entry)
            IsStartupEnabled = !AppServices.StartupManagement.IsDisabled(entry.Name, entry.Location);
    }

    // ── Toggle startup command ────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleStartupAsync()
    {
        if (_record.LinkedStartupEntry is not { } entry) return;

        var enabling = !IsStartupEnabled;

        // Optimistic update — the UI reflects the new state immediately.
        IsStartupEnabled = enabling;

        var success = await Task.Run(() => enabling
            ? AppServices.StartupManagement.EnableEntry(entry.Name, entry.Location)
            : AppServices.StartupManagement.DisableEntry(entry.Name, entry.Location))
            .ConfigureAwait(true);

        // Roll back on registry failure so the toggle stays truthful.
        if (!success)
            IsStartupEnabled = !enabling;
    }
}
