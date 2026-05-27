using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Apps;
using Microsoft.UI.Xaml;

namespace Lucid.ViewModels;

/// <summary>
/// ViewModel for the Apps page.
///
/// Scans installed applications from the Windows Uninstall registry keys on a
/// background thread and joins them with the live startup entry list from
/// <see cref="AppServices.StartupManagement"/>.
///
/// Two observable collections are populated after the scan:
///   <see cref="StartupApps"/> — apps that launch at sign-in, sorted by impact (High first)
///   <see cref="AllApps"/>     — every installed app, alphabetical order
///
/// The scan is triggered by navigating to the page (via <see cref="LoadCommand"/>).
/// It can be re-triggered by the user pressing Refresh.
/// </summary>
public sealed partial class AppsViewModel : ObservableObject
{
    // ── Collections ────────────────────────────────────────────────────────────

    /// <summary>Apps that have a startup registration, sorted High → Moderate → Low → Unknown.</summary>
    public ObservableCollection<AppEntryViewModel> StartupApps { get; } = [];

    /// <summary>All installed apps, alphabetical.</summary>
    public ObservableCollection<AppEntryViewModel> AllApps { get; } = [];

    // ── Stats ──────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _statsText = "";

    // ── Loading state ──────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingVisibility))]
    [NotifyPropertyChangedFor(nameof(ContentVisibility))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartupSectionVisibility))]
    [NotifyPropertyChangedFor(nameof(NoStartupAppsVisibility))]
    private bool _hasStartupApps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAppsVisibility))]
    [NotifyPropertyChangedFor(nameof(NoAppsVisibility))]
    private bool _hasApps;

    // ── Derived visibility ─────────────────────────────────────────────────────

    public Visibility LoadingVisibility       => IsLoading        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ContentVisibility       => IsLoading        ? Visibility.Collapsed : Visibility.Visible;
    public Visibility StartupSectionVisibility => HasStartupApps  ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoStartupAppsVisibility  => HasStartupApps  ? Visibility.Collapsed : Visibility.Visible;
    public Visibility HasAppsVisibility        => HasApps         ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoAppsVisibility         => HasApps         ? Visibility.Collapsed : Visibility.Visible;

    // ── Commands ───────────────────────────────────────────────────────────────

    public IAsyncRelayCommand LoadCommand { get; }

    // ── Construction ──────────────────────────────────────────────────────────

    public AppsViewModel()
    {
        LoadCommand = new AsyncRelayCommand(LoadAsync);
    }

    // ── Load ───────────────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        IsLoading = true;
        StartupApps.Clear();
        AllApps.Clear();

        try
        {
            var records = await Task.Run(() =>
            {
                var entries = AppServices.StartupManagement.GetAllEntries();
                return InstalledAppScanner.Scan(entries);
            }).ConfigureAwait(true);

            foreach (var record in records)
            {
                var vm = new AppEntryViewModel(record);
                AllApps.Add(vm);
                if (record.LinkedStartupEntry is not null)
                    StartupApps.Add(vm);
            }

            HasStartupApps = StartupApps.Count > 0;
            HasApps        = AllApps.Count > 0;
            StatsText      = BuildStatsText(AllApps.Count, StartupApps.Count);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppsVM] LoadAsync failed: {ex}");
            StatsText = "Could not read installed apps.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string BuildStatsText(int total, int startupCount)
    {
        return total == 0
            ? "No applications found"
            : $"{total} installed  ·  {startupCount} launch at startup";
    }
}
