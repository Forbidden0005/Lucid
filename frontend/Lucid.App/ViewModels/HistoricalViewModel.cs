using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Analytics;
using Microsoft.UI.Xaml;

namespace Lucid.ViewModels;

// ── Sub-ViewModel ──────────────────────────────────────────────────────────────

/// <summary>
/// Presentation model for a single recurring insight pattern row.
/// Init-only — the parent ViewModel repopulates the collection wholesale.
/// </summary>
public sealed class RecurringPatternViewModel
{
    public string Title             { get; init; } = "";
    public string OccurrencesText   { get; init; } = "";
    public string FrequencyLabel    { get; init; } = "";
    public string AvgDurationText   { get; init; } = "";
    public string LastSeenText      { get; init; } = "";
    public bool       IsEscalating         { get; init; }
    public string     EscalatingLabel      { get; init; } = "";
    public Visibility EscalatingVisibility { get; init; }

    internal RecurringPatternViewModel(RecurringInsightPattern p)
    {
        Title                = p.Title;
        OccurrencesText      = $"{p.TotalOccurrences} {(p.TotalOccurrences == 1 ? "time" : "times")} in 30 days";
        FrequencyLabel       = p.FrequencyLabel;
        AvgDurationText      = p.AvgDurationMinutes > 0
            ? $"~{p.AvgDurationMinutes:F0} min avg"
            : "Duration not recorded";
        LastSeenText         = $"Last seen {p.LastSeen.LocalDateTime:MMM d 'at' h:mm tt}";
        IsEscalating         = p.IsEscalating;
        EscalatingLabel      = p.IsEscalating ? "Escalating" : "";
        EscalatingVisibility = p.IsEscalating ? Visibility.Visible : Visibility.Collapsed;
    }
}

// ── Main ViewModel ─────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for the Historical Intelligence page.
///
/// Drives:
///   • Long-term health scores (7-day / 30-day) with trend indicators
///   • Per-metric trends (CPU, RAM, Disk — 7-day vs 30-day)
///   • Recurring insight pattern list
///   • Narrative summary paragraph
///   • Coverage metadata (oldest data point, total rows)
///
/// All data comes from <see cref="IHistoricalAnalyticsEngine.ComputeAsync"/>.
/// The page calls InitializeAsync() on load and RefreshCommand on demand.
///
/// Thread safety:
///   RefreshAsync marshals back to the caller's SynchronizationContext.
///   All property sets are safe to call from the UI thread.
/// </summary>
public sealed partial class HistoricalViewModel : ObservableObject
{
    // ── State flags ───────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingVisibility))]
    [NotifyPropertyChangedFor(nameof(NoDataVisibility))]   // NoDataVisibility = !HasData && !IsLoading
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDataVisibility))]
    [NotifyPropertyChangedFor(nameof(NoDataVisibility))]
    private bool _hasData;

    // ── Coverage ──────────────────────────────────────────────────────────────

    [ObservableProperty] private string _oldestDataText  = "—";
    [ObservableProperty] private string _coverageDetail  = "—";

    // ── 7-day health score ────────────────────────────────────────────────────

    [ObservableProperty] private double _sevenDayScore  = 0;
    [ObservableProperty] private string _sevenDayLabel  = "—";
    [ObservableProperty] private string _sevenDayTrend  = "—";
    [ObservableProperty] private string _sevenDayDetail = "—";

    // ── 30-day health score ───────────────────────────────────────────────────

    [ObservableProperty] private double _thirtyDayScore  = 0;
    [ObservableProperty] private string _thirtyDayLabel  = "—";
    [ObservableProperty] private string _thirtyDayTrend  = "—";
    [ObservableProperty] private string _thirtyDayDetail = "—";

    // ── Metric trends ─────────────────────────────────────────────────────────

    [ObservableProperty] private string _cpuTrendLabel  = "—";
    [ObservableProperty] private string _cpuAvgText     = "—";
    [ObservableProperty] private string _ramTrendLabel  = "—";
    [ObservableProperty] private string _ramAvgText     = "—";
    [ObservableProperty] private string _diskTrendLabel = "—";
    [ObservableProperty] private string _diskAvgText    = "—";

    // ── Narrative ─────────────────────────────────────────────────────────────

    [ObservableProperty] private string _narrativeSummary = "";
    [ObservableProperty] private string _computedAtText   = "";

    // ── Pattern visibility ────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoPatternsVisibility))]
    [NotifyPropertyChangedFor(nameof(HasPatternsVisibility))]
    private bool _hasPatterns;

    public Visibility NoPatternsVisibility  => !HasPatterns ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasPatternsVisibility => HasPatterns  ? Visibility.Visible : Visibility.Collapsed;

    // ── Computed visibilities (no converter needed) ───────────────────────────

    public Visibility LoadingVisibility => IsLoading  ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasDataVisibility => HasData    ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoDataVisibility  => !HasData && !IsLoading
                                             ? Visibility.Visible : Visibility.Collapsed;

    // ── Collections ───────────────────────────────────────────────────────────

    public ObservableCollection<RecurringPatternViewModel> Patterns { get; } = new();

    // ── Commands ──────────────────────────────────────────────────────────────

    public IAsyncRelayCommand RefreshCommand { get; }

    // ── Private state ─────────────────────────────────────────────────────────

    private readonly IHistoricalAnalyticsEngine _engine;

    // ── Construction ──────────────────────────────────────────────────────────

    public HistoricalViewModel(IHistoricalAnalyticsEngine engine)
    {
        _engine        = engine;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called from Page_Loaded. Triggers the first analysis pass.
    /// If a cached summary exists it is displayed immediately; a fresh
    /// computation runs in the background and updates the page on completion.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Show cached summary immediately if available (cold-start safe)
        if (_engine.LastSummary is { } cached)
            ApplySummary(cached);

        await RefreshAsync().ConfigureAwait(false);
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    private async Task RefreshAsync()
    {
        IsLoading = true;

        try
        {
            var summary = await _engine.ComputeAsync().ConfigureAwait(false);
            ApplySummary(summary);
        }
        catch
        {
            // Best-effort: leave stale data visible rather than crashing the page
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Summary application ───────────────────────────────────────────────────

    private void ApplySummary(HistoricalSummary summary)
    {
        HasData = summary.TotalTelemetryRows > 0 || summary.TotalInsightEvents > 0;

        // Coverage
        OldestDataText = summary.OldestDataPoint.HasValue
            ? $"Data since {summary.OldestDataPoint.Value.LocalDateTime:MMM d, yyyy}"
            : "No historical data yet";

        CoverageDetail = summary.TotalTelemetryRows > 0
            ? $"{summary.TotalTelemetryRows:N0} telemetry samples · " +
              $"{summary.TotalInsightEvents:N0} insight events · " +
              $"{summary.TotalTimelineEvents:N0} timeline events"
            : "Start the app and leave it running to begin building historical data.";

        // 7-day health
        SevenDayScore  = summary.SevenDayHealth.Score;
        SevenDayLabel  = summary.SevenDayHealth.Label;
        SevenDayTrend  = FormatTrend(summary.SevenDayHealth.Trend);
        SevenDayDetail = FormatHealthDetail(summary.SevenDayHealth);

        // 30-day health
        ThirtyDayScore  = summary.ThirtyDayHealth.Score;
        ThirtyDayLabel  = summary.ThirtyDayHealth.Label;
        ThirtyDayTrend  = FormatTrend(summary.ThirtyDayHealth.Trend);
        ThirtyDayDetail = FormatHealthDetail(summary.ThirtyDayHealth);

        // Metric trends
        CpuTrendLabel  = summary.CpuTrend.TrendLabel;
        CpuAvgText     = FormatMetricAvg(summary.CpuTrend);
        RamTrendLabel  = summary.RamTrend.TrendLabel;
        RamAvgText     = FormatMetricAvg(summary.RamTrend);
        DiskTrendLabel = summary.DiskTrend.TrendLabel;
        DiskAvgText    = FormatMetricAvg(summary.DiskTrend);

        // Narrative
        NarrativeSummary = summary.NarrativeSummary;
        ComputedAtText   = $"Updated {summary.ComputedAt.LocalDateTime:h:mm tt · MMM d}";

        // Patterns
        Patterns.Clear();
        foreach (var p in summary.RecurringPatterns)
            Patterns.Add(new RecurringPatternViewModel(p));
        HasPatterns = Patterns.Count > 0;
    }

    // ── Formatters ────────────────────────────────────────────────────────────

    private static string FormatTrend(TrendDirection dir) => dir switch
    {
        TrendDirection.Improving => "▲ Improving",
        TrendDirection.Worsening => "▼ Worsening",
        _                        => "— Stable",
    };

    private static string FormatHealthDetail(HealthScore h)
    {
        var parts = new List<string>();
        if (h.WarningCount > 0)
            parts.Add($"{h.WarningCount} {(h.WarningCount == 1 ? "warning" : "warnings")}");
        if (h.RecommendationCount > 0)
            parts.Add($"{h.RecommendationCount} {(h.RecommendationCount == 1 ? "recommendation" : "recommendations")}");
        return parts.Count > 0 ? string.Join(" · ", parts) : "No issues detected";
    }

    private static string FormatMetricAvg(LongTermMetricTrend t)
    {
        if (t.SevenDayAvg.HasValue && t.ThirtyDayAvg.HasValue)
            return $"~{t.SevenDayAvg.Value:F0}% (7d avg) · ~{t.ThirtyDayAvg.Value:F0}% (30d avg)";
        if (t.SevenDayAvg.HasValue)
            return $"~{t.SevenDayAvg.Value:F0}% (7d avg) — limited history";
        return "Insufficient data";
    }
}
