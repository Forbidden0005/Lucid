using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Watchtower;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Lucid.ViewModels;

// ── Sub-ViewModels ────────────────────────────────────────────────────────────

/// <summary>ViewModel for a single watchtower alert card.</summary>
public sealed class AlertCardViewModel
{
    public string AlertId          { get; init; } = string.Empty;
    public string Title            { get; init; } = string.Empty;
    public string Explanation      { get; init; } = string.Empty;
    public string SeverityLabel    { get; init; } = string.Empty;
    public string SeverityColor    { get; init; } = "#6B7280";
    public string SeverityGlyph    { get; init; } = "";
    public string TimeText         { get; init; } = string.Empty;
    public string ConfidenceText   { get; init; } = string.Empty;
    public string     ActionHint              { get; init; } = string.Empty;
    public bool       HasActionHint           { get; init; }
    public Visibility HasActionHintVisibility => HasActionHint ? Visibility.Visible : Visibility.Collapsed;
    public string     AccentColor             { get; init; } = "#6B7280";
}

/// <summary>ViewModel for a metric drift row.</summary>
public sealed class DriftRowViewModel
{
    public string MetricName     { get; init; } = string.Empty;
    public string Glyph          { get; init; } = string.Empty;
    public string DriftLabel     { get; init; } = string.Empty;
    public string DriftColor     { get; init; } = "#6B7280";
    public string SevenDayText   { get; init; } = string.Empty;
    public string ThirtyDayText  { get; init; } = string.Empty;
    public bool   IsSignificant  { get; init; }
    public string SignificanceIndicator { get; init; } = string.Empty;
}

/// <summary>ViewModel for a stability risk forecast card.</summary>
public sealed class ForecastCardViewModel
{
    public string ForecastId         { get; init; } = string.Empty;
    public string Title              { get; init; } = string.Empty;
    public string Description        { get; init; } = string.Empty;
    public string DaysToImpactText   { get; init; } = string.Empty;
    public string RiskScoreText      { get; init; } = string.Empty;
    public string RiskColor          { get; init; } = "#6B7280";
    public string ConfidenceText     { get; init; } = string.Empty;
    public string SignalsText        { get; init; } = string.Empty;
}

/// <summary>ViewModel for a maintenance window recommendation card.</summary>
public sealed class MaintenanceCardViewModel
{
    public string WindowId           { get; init; } = string.Empty;
    public string Title              { get; init; } = string.Empty;
    public string Rationale          { get; init; } = string.Empty;
    public string TimingText         { get; init; } = string.Empty;
    public string PriorityLabel      { get; init; } = string.Empty;
    public string PriorityColor      { get; init; } = "#6B7280";
    public string PriorityGlyph      { get; init; } = "";
}

/// <summary>ViewModel for a ranked intervention suggestion.</summary>
public sealed class InterventionCardViewModel
{
    public string SuggestionId       { get; init; } = string.Empty;
    public string Title              { get; init; } = string.Empty;
    public string Rationale          { get; init; } = string.Empty;
    public string EffectivenessLabel { get; init; } = string.Empty;
    public string EffectivenessColor { get; init; } = "#6B7280";
    public string PriorityBadge      { get; init; } = string.Empty;
    public string ConfidenceText     { get; init; } = string.Empty;
}

// ── Main ViewModel ────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for the Operational Watchtower page.
///
/// Owns the page lifecycle:
///   • On page load → triggers initial refresh
///   • Subscribes to SnapshotUpdated → updates all observable collections
///   • Exposes Visibility properties (no XAML converters)
///   • RefreshCommand → manual refresh
///
/// Threading: all UI updates marshal via DispatcherQueue captured in constructor.
/// </summary>
public sealed partial class WatchtowerViewModel : ObservableObject
{
    private readonly OperationalWatchtowerService _service;
    private readonly DispatcherQueue              _ui;

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingVisibility))]
    [NotifyPropertyChangedFor(nameof(ContentVisibility))]
    [NotifyPropertyChangedFor(nameof(NoDataVisibility))]   // NoDataVisibility = !IsLoading && !HasData
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoDataVisibility))]
    [NotifyPropertyChangedFor(nameof(ContentVisibility))]
    [NotifyPropertyChangedFor(nameof(SufficientHistoryVisibility))] // SufficientHistoryVisibility = !ShowInsufficientHistory && HasData
    private bool _hasData;

    [ObservableProperty]
    private bool _hasSufficientHistory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InsufficientHistoryVisibility))]
    [NotifyPropertyChangedFor(nameof(SufficientHistoryVisibility))]
    private bool _showInsufficientHistory;

    // ── Outlook ───────────────────────────────────────────────────────────────
    [ObservableProperty] private int    _outlookScore;
    [ObservableProperty] private string _outlookLabel         = "—";
    [ObservableProperty] private string _outlookColor         = "#6B7280";
    [ObservableProperty] private string _outlookNarrative     = string.Empty;
    [ObservableProperty] private string _lastComputedText     = string.Empty;

    // ── Alerts section ────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoAlertsVisibility))]
    [NotifyPropertyChangedFor(nameof(AlertsListVisibility))]
    private bool _hasAlerts;

    [ObservableProperty] private string _alertsSummaryText = string.Empty;

    // ── Drift section ─────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoDriftVisibility))]
    [NotifyPropertyChangedFor(nameof(DriftListVisibility))]
    private bool _hasDrift;

    // ── Forecasts section ─────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoForecastsVisibility))]
    [NotifyPropertyChangedFor(nameof(ForecastsListVisibility))]
    private bool _hasForecasts;

    // ── Maintenance section ───────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoMaintenanceVisibility))]
    [NotifyPropertyChangedFor(nameof(MaintenanceListVisibility))]
    private bool _hasMaintenance;

    // ── Interventions section ─────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoInterventionsVisibility))]
    [NotifyPropertyChangedFor(nameof(InterventionsListVisibility))]
    private bool _hasInterventions;

    // ── Collections ───────────────────────────────────────────────────────────
    public ObservableCollection<AlertCardViewModel>       Alerts       { get; } = [];
    public ObservableCollection<DriftRowViewModel>        DriftRows    { get; } = [];
    public ObservableCollection<ForecastCardViewModel>    Forecasts    { get; } = [];
    public ObservableCollection<MaintenanceCardViewModel> Maintenance  { get; } = [];
    public ObservableCollection<InterventionCardViewModel> Interventions { get; } = [];

    // ── Visibility helpers ────────────────────────────────────────────────────
    public Visibility LoadingVisibility          => IsLoading                                     ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ContentVisibility          => !IsLoading && HasData                         ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoDataVisibility           => !IsLoading && !HasData                        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InsufficientHistoryVisibility => ShowInsufficientHistory                    ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SufficientHistoryVisibility   => !ShowInsufficientHistory && HasData        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoAlertsVisibility         => !HasAlerts                                    ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AlertsListVisibility       => HasAlerts                                     ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoDriftVisibility          => !HasDrift                                     ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DriftListVisibility        => HasDrift                                      ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoForecastsVisibility      => !HasForecasts                                 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ForecastsListVisibility    => HasForecasts                                  ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoMaintenanceVisibility    => !HasMaintenance                               ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MaintenanceListVisibility  => HasMaintenance                                ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoInterventionsVisibility  => !HasInterventions                             ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InterventionsListVisibility => HasInterventions                             ? Visibility.Visible : Visibility.Collapsed;

    // ── Constructor ───────────────────────────────────────────────────────────

    public WatchtowerViewModel(OperationalWatchtowerService service, DispatcherQueue uiDispatcher)
    {
        _service = service;
        _ui      = uiDispatcher;

        _service.SnapshotUpdated += OnSnapshotUpdated;

        // If a snapshot already exists (e.g. user navigated away and back), show it immediately
        if (_service.LastSnapshot is { } existing)
            ApplySnapshot(existing);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        HasData   = false;

        try
        {
            await _service.Coordinator.RefreshAsync().ConfigureAwait(true);

            // If still no snapshot after refresh, show cold-start state
            if (_service.LastSnapshot is null)
            {
                IsLoading = false;
                HasData   = false;
            }
            // When a snapshot arrives the SnapshotUpdated event sets IsLoading = false
        }
        catch (Exception ex)
        {
            // Coordinator refresh failed — reset state so the page is not stuck
            // in a permanent loading spinner, and log the cause for diagnostics.
            Lucid.Services.Diagnostics.Logging.OperationalDiagnostics.ReportFailure("WatchtowerVM", "RefreshAsync failed", ex);
            IsLoading = false;
            HasData   = false;
        }
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    /// <summary>Unsubscribes from service events. Call from OnNavigatedFrom.</summary>
    public void Cleanup()
    {
        _service.SnapshotUpdated -= OnSnapshotUpdated;
    }

    // ── Snapshot handling ─────────────────────────────────────────────────────

    private void OnSnapshotUpdated(object? sender, WatchtowerSnapshot snapshot)
    {
        _ui.TryEnqueue(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(WatchtowerSnapshot snapshot)
    {
        // ── Outlook ───────────────────────────────────────────────────────────
        OutlookScore       = snapshot.Outlook.OverallScore;
        OutlookLabel       = snapshot.Outlook.Label;
        OutlookColor       = snapshot.Outlook.Color;
        OutlookNarrative   = snapshot.Outlook.NarrativeSummary;
        LastComputedText   = $"Last updated {FormatRelativeTime(snapshot.ComputedAt)}";

        HasSufficientHistory   = snapshot.HasSufficientHistory;
        ShowInsufficientHistory = !snapshot.HasSufficientHistory;

        // ── Alerts ────────────────────────────────────────────────────────────
        Alerts.Clear();
        foreach (var alert in snapshot.Alerts)
        {
            Alerts.Add(new AlertCardViewModel
            {
                AlertId        = alert.AlertId,
                Title          = alert.Title,
                Explanation    = alert.Explanation,
                SeverityLabel  = SeverityToLabel(alert.Severity),
                SeverityColor  = SeverityToColor(alert.Severity),
                SeverityGlyph  = SeverityToGlyph(alert.Severity),
                AccentColor    = SeverityToColor(alert.Severity),
                TimeText       = FormatRelativeTime(alert.DetectedAt),
                ConfidenceText = $"{alert.ConfidencePercent}% confidence",
                ActionHint     = alert.ActionHint ?? string.Empty,
                HasActionHint  = !string.IsNullOrEmpty(alert.ActionHint),
            });
        }
        HasAlerts       = Alerts.Count > 0;
        AlertsSummaryText = Alerts.Count switch
        {
            0 => "No active patterns detected",
            1 => "1 pattern detected",
            _ => $"{Alerts.Count} patterns detected",
        };

        // ── Drift ─────────────────────────────────────────────────────────────
        DriftRows.Clear();
        foreach (var drift in snapshot.DriftObservations)
        {
            DriftRows.Add(new DriftRowViewModel
            {
                MetricName    = drift.MetricName,
                Glyph         = drift.Glyph,
                DriftLabel    = drift.DriftLabel,
                DriftColor    = drift.DriftColor,
                SevenDayText  = drift.SevenDayAvg.HasValue
                    ? $"{drift.SevenDayAvg:F0}% avg"
                    : "—",
                ThirtyDayText = drift.ThirtyDayAvg.HasValue
                    ? $"{drift.ThirtyDayAvg:F0}% avg"
                    : "—",
                IsSignificant = drift.IsSignificant,
                SignificanceIndicator = drift.IsSignificant ? "●" : string.Empty,
            });
        }
        HasDrift = DriftRows.Count > 0;

        // ── Forecasts ─────────────────────────────────────────────────────────
        Forecasts.Clear();
        foreach (var forecast in snapshot.Forecasts)
        {
            Forecasts.Add(new ForecastCardViewModel
            {
                ForecastId       = forecast.ForecastId,
                Title            = forecast.Title,
                Description      = forecast.Description,
                DaysToImpactText = forecast.DaysToImpact.HasValue
                    ? $"Est. {forecast.DaysToImpact} days"
                    : "Timeline uncertain",
                RiskScoreText    = $"{forecast.RiskScore}/100",
                RiskColor        = RiskScoreToColor(forecast.RiskScore),
                ConfidenceText   = $"{forecast.ConfidencePercent}% confidence",
                SignalsText      = string.Join(" · ", forecast.ContributingSignals.Take(3)),
            });
        }
        HasForecasts = Forecasts.Count > 0;

        // ── Maintenance ───────────────────────────────────────────────────────
        Maintenance.Clear();
        foreach (var maint in snapshot.MaintenanceWindows)
        {
            Maintenance.Add(new MaintenanceCardViewModel
            {
                WindowId      = maint.WindowId,
                Title         = maint.Title,
                Rationale     = maint.Rationale,
                TimingText    = maint.SuggestedTimeDescription,
                PriorityLabel = PriorityToLabel(maint.Priority),
                PriorityColor = PriorityToColor(maint.Priority),
                PriorityGlyph = PriorityToGlyph(maint.Priority),
            });
        }
        HasMaintenance = Maintenance.Count > 0;

        // ── Interventions ─────────────────────────────────────────────────────
        Interventions.Clear();
        foreach (var intervention in snapshot.Interventions)
        {
            Interventions.Add(new InterventionCardViewModel
            {
                SuggestionId       = intervention.SuggestionId,
                Title              = intervention.Title,
                Rationale          = intervention.Rationale,
                EffectivenessLabel = intervention.EffectivenessLabel,
                EffectivenessColor = EffectivenessToColor(intervention.EffectivenessLabel),
                PriorityBadge      = $"#{intervention.Priority}",
                ConfidenceText     = $"{intervention.ConfidencePercent}% confidence",
            });
        }
        HasInterventions = Interventions.Count > 0;

        // ── Final state ───────────────────────────────────────────────────────
        IsLoading = false;
        HasData   = true;
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    private static string FormatRelativeTime(DateTimeOffset time)
    {
        var delta = DateTimeOffset.Now - time;
        if (delta.TotalSeconds < 60)  return "just now";
        if (delta.TotalMinutes < 60)  return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalHours   < 24)  return $"{(int)delta.TotalHours}h ago";
        return time.ToString("MMM d");
    }

    private static string SeverityToLabel(WatchtowerAlertSeverity s) => s switch
    {
        WatchtowerAlertSeverity.Elevated => "Elevated",
        WatchtowerAlertSeverity.Warning  => "Warning",
        WatchtowerAlertSeverity.Advisory => "Advisory",
        _                                => "Info",
    };

    private static string SeverityToColor(WatchtowerAlertSeverity s) => s switch
    {
        WatchtowerAlertSeverity.Elevated => "#EF4444",
        WatchtowerAlertSeverity.Warning  => "#F59E0B",
        WatchtowerAlertSeverity.Advisory => "#60A5FA",
        _                                => "#6B7280",
    };

    private static string SeverityToGlyph(WatchtowerAlertSeverity s) => s switch
    {
        WatchtowerAlertSeverity.Elevated => "",
        WatchtowerAlertSeverity.Warning  => "",
        WatchtowerAlertSeverity.Advisory => "",
        _                                => "",
    };

    private static string RiskScoreToColor(int score) => score switch
    {
        >= 70 => "#EF4444",
        >= 50 => "#F59E0B",
        >= 30 => "#60A5FA",
        _     => "#6B7280",
    };

    private static string PriorityToLabel(MaintenancePriority p) => p switch
    {
        MaintenancePriority.Timely      => "Timely",
        MaintenancePriority.Recommended => "Recommended",
        _                               => "Routine",
    };

    private static string PriorityToColor(MaintenancePriority p) => p switch
    {
        MaintenancePriority.Timely      => "#F59E0B",
        MaintenancePriority.Recommended => "#60A5FA",
        _                               => "#6B7280",
    };

    private static string PriorityToGlyph(MaintenancePriority p) => p switch
    {
        MaintenancePriority.Timely      => "",
        MaintenancePriority.Recommended => "",
        _                               => "",
    };

    private static string EffectivenessToColor(string label)
    {
        if (label.Contains("effective", StringComparison.OrdinalIgnoreCase)) return "#34D399";
        if (label.Contains("Mixed", StringComparison.OrdinalIgnoreCase))     return "#F59E0B";
        if (label.Contains("Low", StringComparison.OrdinalIgnoreCase))       return "#EF4444";
        return "#6B7280";
    }
}
