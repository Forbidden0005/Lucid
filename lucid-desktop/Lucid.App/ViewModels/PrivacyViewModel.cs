using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Privacy;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lucid.ViewModels;

// ── Shared brush palette ───────────────────────────────────────────────────────
// Allocated once — reused across every category and app entry to avoid
// per-item heap allocations during collection population.

internal static class PrivacyBrushPalette
{
    public static readonly SolidColorBrush Allow   = new(Color.FromArgb(255, 0x22, 0xC5, 0x5E)); // Green
    public static readonly SolidColorBrush Deny    = new(Color.FromArgb(255, 0xEF, 0x44, 0x44)); // Red
    public static readonly SolidColorBrush Warning = new(Color.FromArgb(255, 0xF5, 0x9E, 0x0B)); // Amber
    public static readonly SolidColorBrush Neutral = new(Color.FromArgb(255, 0x6B, 0x72, 0x80)); // Gray
    public static readonly SolidColorBrush Blue    = new(Color.FromArgb(255, 0x6C, 0xA3, 0xF8)); // Blue
}

// ── Sub-ViewModels ─────────────────────────────────────────────────────────────
// Init-only — the parent ViewModel repopulates the collections wholesale
// on each load. These classes do not implement INotifyPropertyChanged.

/// <summary>
/// Display model for a single app's grant record within one permission category.
///
/// Observable: <see cref="IsAllowed"/> changes when the user toggles the permission,
/// propagating immediately to <see cref="StatusText"/>, <see cref="StatusBrush"/>,
/// and <see cref="ToggleLabel"/> via NotifyPropertyChangedFor.
/// </summary>
public sealed partial class PrivacyAppEntryViewModel : ObservableObject
{
    // ── Static / non-mutable ───────────────────────────────────────────────────

    /// <summary>Capability registry key (e.g. "webcam"). Used by the toggle command.</summary>
    public string    PermissionType    { get; }
    /// <summary>App registry subkey identifier. Used by the toggle command.</summary>
    public string    AppIdentifier     { get; }
    public string    AppDisplayName    { get; }
    public bool      IsPackaged        { get; }
    /// <summary>"Modern App" or "Desktop App"</summary>
    public string    AppTypeLabel      { get; }
    /// <summary>Relative time string, e.g. "2d ago", or empty when never used.</summary>
    public string    LastUsedText      { get; }
    public Visibility LastUsedVisibility { get; }

    // ── Observable (changes on toggle) ────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    [NotifyPropertyChangedFor(nameof(ToggleLabel))]
    private bool _isAllowed;

    /// <summary>"Allowed" or "Denied" — updates when IsAllowed changes.</summary>
    public string          StatusText  => IsAllowed ? "Allowed" : "Denied";
    /// <summary>Green when allowed, red when denied.</summary>
    public SolidColorBrush StatusBrush => IsAllowed ? PrivacyBrushPalette.Allow : PrivacyBrushPalette.Deny;
    /// <summary>Button label: "Revoke" when allowed, "Restore" when denied.</summary>
    public string          ToggleLabel => IsAllowed ? "Revoke" : "Restore";

    // ── Construction ──────────────────────────────────────────────────────────

    internal PrivacyAppEntryViewModel(PrivacyPermissionRecord r)
    {
        PermissionType     = r.PermissionType;
        AppIdentifier      = r.AppIdentifier;
        AppDisplayName     = r.AppDisplayName;
        _isAllowed         = r.IsAllowed;
        IsPackaged         = r.IsPackaged;
        AppTypeLabel       = r.IsPackaged ? "Modern App" : "Desktop App";
        LastUsedText       = r.LastUsed.HasValue ? FormatRelativeTime(r.LastUsed.Value) : "";
        LastUsedVisibility = r.LastUsed.HasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Toggle command ────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the new Allow/Deny value to the ConsentStore registry key.
    /// On success, flips <see cref="IsAllowed"/> so the UI updates immediately.
    /// On failure (denied, Group Policy, etc.) leaves the UI unchanged.
    /// </summary>
    [RelayCommand]
    private async Task TogglePermissionAsync()
    {
        var newState = !IsAllowed;
        var success  = await Task.Run(
            () => PrivacyPermissionWriter.TrySetAppPermission(
                      PermissionType, AppIdentifier, newState))
            .ConfigureAwait(true);  // marshal back to UI thread for property set

        if (success)
            IsAllowed = newState;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatRelativeTime(DateTimeOffset t)
    {
        var age = DateTimeOffset.Now - t;
        if (age.TotalMinutes < 2)   return "just now";
        if (age.TotalHours   < 1)   return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalDays    < 1)   return $"{(int)age.TotalHours}h ago";
        if (age.TotalDays    < 30)  return $"{(int)age.TotalDays}d ago";
        if (age.TotalDays    < 365) return $"{(int)(age.TotalDays / 30)}mo ago";
        return $"{(int)(age.TotalDays / 365)}y ago";
    }
}

/// <summary>Display model for one privacy permission category (Camera, Microphone, …).</summary>
public sealed class PrivacyCategoryViewModel
{
    public string          DisplayName       { get; init; } = "";
    public string          Glyph             { get; init; } = "";
    public bool            GloballyEnabled   { get; init; }

    /// <summary>"System on" or "System off" — reflects the master OS toggle.</summary>
    public string          GlobalStatusText  { get; init; } = "";

    /// <summary>e.g. "5 apps"</summary>
    public string          CountText         { get; init; } = "";

    /// <summary>e.g. "4 allowed · 1 denied"</summary>
    public string          SummaryText       { get; init; } = "";

    /// <summary>Green when globally on, amber when globally off.</summary>
    public SolidColorBrush GlobalStatusBrush { get; init; } = PrivacyBrushPalette.Neutral;

    /// <summary>Left accent stripe: blue when on, amber when globally off.</summary>
    public SolidColorBrush AccentBrush       { get; init; } = PrivacyBrushPalette.Neutral;

    public ObservableCollection<PrivacyAppEntryViewModel> Apps { get; }

    internal PrivacyCategoryViewModel(PrivacyCategoryRecord cat)
    {
        DisplayName       = cat.DisplayName;
        Glyph             = cat.Glyph;
        GloballyEnabled   = cat.GloballyEnabled;
        GlobalStatusText  = cat.GloballyEnabled ? "System on" : "System off";
        GlobalStatusBrush = cat.GloballyEnabled ? PrivacyBrushPalette.Allow   : PrivacyBrushPalette.Warning;
        AccentBrush       = cat.GloballyEnabled ? PrivacyBrushPalette.Blue    : PrivacyBrushPalette.Warning;

        var allowedCount = cat.Apps.Count(a => a.IsAllowed);
        var deniedCount  = cat.Apps.Count - allowedCount;
        CountText        = $"{cat.Apps.Count} {(cat.Apps.Count == 1 ? "app" : "apps")}";
        SummaryText      = BuildSummary(allowedCount, deniedCount);

        Apps = new ObservableCollection<PrivacyAppEntryViewModel>(
            cat.Apps.Select(a => new PrivacyAppEntryViewModel(a)));
    }

    private static string BuildSummary(int allowed, int denied)
    {
        if (allowed == 0 && denied == 0) return "No apps registered";
        if (denied  == 0) return $"{allowed} allowed";
        if (allowed == 0) return $"{denied} denied";
        return $"{allowed} allowed · {denied} denied";
    }
}

// ── Main ViewModel ─────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for the Privacy page.
///
/// Reads the Windows CapabilityAccessManager ConsentStore on a background thread
/// and populates an observable collection of <see cref="PrivacyCategoryViewModel"/>
/// instances — one per permission type that at least one app has requested.
///
/// The load is triggered by navigating to the page (via <see cref="LoadCommand"/>)
/// and can be re-triggered with the Refresh button.
/// </summary>
public sealed partial class PrivacyViewModel : ObservableObject
{
    // ── Collections ────────────────────────────────────────────────────────────

    /// <summary>One entry per permission category, sorted by app count descending.</summary>
    public ObservableCollection<PrivacyCategoryViewModel> Categories { get; } = [];

    // ── Stats ──────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _statsText = "";

    // ── Loading state ──────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingVisibility))]
    [NotifyPropertyChangedFor(nameof(ContentVisibility))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCategoriesVisibility))]
    [NotifyPropertyChangedFor(nameof(NoCategoriesVisibility))]
    private bool _hasCategories;

    // ── Derived visibility ─────────────────────────────────────────────────────

    public Visibility LoadingVisibility      => IsLoading      ? Visibility.Visible   : Visibility.Collapsed;
    public Visibility ContentVisibility      => IsLoading      ? Visibility.Collapsed : Visibility.Visible;
    public Visibility HasCategoriesVisibility => HasCategories ? Visibility.Visible   : Visibility.Collapsed;
    public Visibility NoCategoriesVisibility  => HasCategories ? Visibility.Collapsed : Visibility.Visible;

    // ── Commands ───────────────────────────────────────────────────────────────

    public IAsyncRelayCommand LoadCommand { get; }

    // ── Construction ──────────────────────────────────────────────────────────

    public PrivacyViewModel()
    {
        LoadCommand = new AsyncRelayCommand(LoadAsync);
    }

    // ── Load ───────────────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        IsLoading = true;
        Categories.Clear();

        try
        {
            var records = await Task.Run(PrivacyPermissionScanner.Scan).ConfigureAwait(true);

            foreach (var cat in records)
                Categories.Add(new PrivacyCategoryViewModel(cat));

            HasCategories = Categories.Count > 0;

            if (Categories.Count == 0)
            {
                StatsText = "No privacy permission data found on this machine.";
            }
            else
            {
                var totalApps   = Categories.Sum(c => c.Apps.Count);
                var totalGrants = Categories.Sum(c => c.Apps.Count(a => a.IsAllowed));
                StatsText = $"{Categories.Count} permissions tracked  ·  {totalApps} total requests  ·  {totalGrants} active grants";
            }
        }
        catch (Exception ex)
        {
            Lucid.Services.Diagnostics.Logging.OperationalDiagnostics.ReportFailure("PrivacyVM", "LoadAsync failed", ex);
            StatsText = "Could not read privacy permission data.";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
