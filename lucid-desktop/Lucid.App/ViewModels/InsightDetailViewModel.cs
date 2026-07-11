using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services;
using Lucid.Services.Analytics;
using Lucid.Services.Baseline;
using Lucid.Services.History;
using Lucid.Services.Intelligence;
using Lucid.Services.Narrative;
using Lucid.Services.Persistence;
using Lucid.Services.Telemetry;
using Lucid.Services.Timeline;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lucid.ViewModels;

/// <summary>
/// Drives the InsightDetailPage — the full investigation workspace for a
/// single intelligence finding.
///
/// Architecture
/// ────────────
/// Loaded once via <see cref="Load"/> with an insight rule ID (e.g.
/// "cpu.sustained-high"). The ViewModel subscribes to the live intelligence
/// engine and narrative engine so both the chart and prose stay current as
/// telemetry ticks arrive.
///
/// Chart data is rebuilt whenever the selected time window changes or the
/// intelligence engine publishes a new snapshot.
///
/// Data sources
/// ────────────
///   AppServices.Intelligence  — current SystemInsight + live updates
///   AppServices.History       — telemetry samples for the chart
///   AppServices.Baseline      — machine-learned baseline for the band overlay
///   AppServices.Timeline      — related timeline events (filtered by RelatedInsightId)
///   AppServices.HistoryService — action / remediation history (async)
///   AppServices.Narrative     — current operational narrative prose
///   AppServices.InsightHistory — persisted onset/resolution rows, used as a
///                                fallback when the finding is not currently
///                                active (e.g. deep-linked from the
///                                health-score breakdown after it resolved)
///
/// Cleanup
/// ───────
/// Call <see cref="Cleanup"/> from the page's Unloaded event.
/// </summary>
public sealed partial class InsightDetailViewModel : ObservableObject
{
    // ── Injected services ─────────────────────────────────────────────────────

    private readonly ISystemInsightEngine        _intelligence;
    private readonly IOperationalNarrativeEngine _narrativeEngine;
    private readonly ITelemetryHistoryBuffer     _history;
    private readonly ISystemBaselineService      _baseline;
    private readonly ITimelineAggregationService _timeline;
    private readonly IOperationHistoryService    _historyService;
    private readonly InsightHistoryRepository    _insightHistory;

    // ── State ─────────────────────────────────────────────────────────────────

    private string         _insightId = string.Empty;
    private SystemInsight? _insight;

    /// <summary>
    /// Persisted context for a finding that is not currently active — its real
    /// title, severity, and occurrence range from the SQLite insight history.
    /// Populated by <see cref="LoadHistoricalContextAsync"/> so a resolved
    /// finding shows what it was, not a bare "Finding Cleared" placeholder.
    /// </summary>
    private HealthScoreContribution? _historical;

    // ── Static brush palette ──────────────────────────────────────────────────

    private static readonly SolidColorBrush s_warningAccent = Mk(255, 255, 179,  71);
    private static readonly SolidColorBrush s_infoAccent    = Mk(255,  78, 161, 255);
    private static readonly SolidColorBrush s_goodAccent    = Mk(255,  87, 214, 141);

    private static readonly SolidColorBrush s_warningBg = Mk(0x26, 255, 179,  71);
    private static readonly SolidColorBrush s_infoBg    = Mk(0x26,  78, 161, 255);
    private static readonly SolidColorBrush s_goodBg    = Mk(0x26,  87, 214, 141);

    // Time-window chip brushes
    private static readonly SolidColorBrush s_winActiveBg   = Mk(0x26,  78, 161, 255);
    private static readonly SolidColorBrush s_winInactiveBg = Mk(0x0A, 255, 255, 255);
    private static readonly SolidColorBrush s_winActiveFg   = Mk(0xFF,  78, 161, 255);
    private static readonly SolidColorBrush s_winInactiveFg = Mk(0x80, 200, 200, 200);

    private static SolidColorBrush Mk(byte a, byte r, byte g, byte b) =>
        new(Color.FromArgb(a, r, g, b));

    // ── Time window selection ─────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Win1MinBg))]
    [NotifyPropertyChangedFor(nameof(Win5MinBg))]
    [NotifyPropertyChangedFor(nameof(Win15MinBg))]
    [NotifyPropertyChangedFor(nameof(Win30MinBg))]
    [NotifyPropertyChangedFor(nameof(Win1MinFg))]
    [NotifyPropertyChangedFor(nameof(Win5MinFg))]
    [NotifyPropertyChangedFor(nameof(Win15MinFg))]
    [NotifyPropertyChangedFor(nameof(Win30MinFg))]
    private int _selectedWindowIndex = 1;   // default: 5 min

    public SolidColorBrush Win1MinBg  => WinBg(0);
    public SolidColorBrush Win5MinBg  => WinBg(1);
    public SolidColorBrush Win15MinBg => WinBg(2);
    public SolidColorBrush Win30MinBg => WinBg(3);
    public SolidColorBrush Win1MinFg  => WinFg(0);
    public SolidColorBrush Win5MinFg  => WinFg(1);
    public SolidColorBrush Win15MinFg => WinFg(2);
    public SolidColorBrush Win30MinFg => WinFg(3);

    private SolidColorBrush WinBg(int i) => SelectedWindowIndex == i ? s_winActiveBg : s_winInactiveBg;
    private SolidColorBrush WinFg(int i) => SelectedWindowIndex == i ? s_winActiveFg : s_winInactiveFg;

    [RelayCommand] private void SelectWin1Min()  => SelectedWindowIndex = 0;
    [RelayCommand] private void SelectWin5Min()  => SelectedWindowIndex = 1;
    [RelayCommand] private void SelectWin15Min() => SelectedWindowIndex = 2;
    [RelayCommand] private void SelectWin30Min() => SelectedWindowIndex = 3;

    partial void OnSelectedWindowIndexChanged(int value) => RebuildChart();

    // ── Identity ──────────────────────────────────────────────────────────────

    public string  InsightId       => _insightId;
    public bool    IsInsightActive => _insight is not null;

    public Visibility ActiveVisibility  => Vis(IsInsightActive);
    public Visibility ClearedVisibility => Vis(!IsInsightActive);

    // ── Insight header ────────────────────────────────────────────────────────

    public string IconGlyph => _insightId switch
    {
        "cpu.sustained-high"   => "",
        "cpu.high-temperature" => "",
        "ram.high-pressure"    => "",
        "gpu.unexpected-load"  => "",
        "disk.low-space"       => "",
        "disk.sustained-io"    => "",
        "system.healthy"       => "",
        _                      => "",
    };

    public SolidColorBrush AccentBrush            => Accent(EffectiveSeverity, _insightId);
    public SolidColorBrush BadgeBackground        => Bg(EffectiveSeverity, _insightId);
    public SolidColorBrush IconContainerBg        => Bg(EffectiveSeverity, _insightId);

    public string          Title           => _insight?.Title  ?? _historical?.Title ?? "Finding Cleared";
    public string          Detail          => _insight?.Detail ?? HistoricalDetail   ?? "This finding is no longer active.";
    public string?         ActionHint      => _insight?.ActionHint;
    public string          SeverityLabel
    {
        get
        {
            string label = EffectiveSeverity switch
            {
                InsightSeverity.Warning        => "Warning",
                InsightSeverity.Recommendation => "Tip",
                _                              => "Info",
            };
            // A finding shown purely from history has resolved — say so in the
            // chip rather than presenting a stale severity as current.
            return _insight is null && _historical is not null ? $"{label} · resolved" : label;
        }
    }
    public int             ConfidencePercent => _insight?.ConfidencePercent ?? 0;

    /// <summary>Confidence applies to live findings only — a resolved finding shows "—".</summary>
    public string          ConfidenceText    => _insight is null ? "—" : $"{ConfidencePercent}%";
    public string          RelativeTime      { get; private set; } = string.Empty;
    public string          RuleIdDisplay     => _insightId;

    /// <summary>
    /// Severity used for the chip and brushes: the live finding's severity when
    /// active, else the highest severity recorded in its persisted history.
    /// </summary>
    private InsightSeverity EffectiveSeverity =>
        _insight?.Severity
        ?? _historical?.SeverityOrdinal switch
        {
            >= 2 => InsightSeverity.Warning,
            1    => InsightSeverity.Recommendation,
            _    => InsightSeverity.Info,
        };

    /// <summary>
    /// Plain-language summary of a resolved finding's history, e.g.
    /// "This condition is not active right now. It was observed 9 times
    /// between Jul 6 and Jul 10." Null until history has loaded.
    /// </summary>
    private string? HistoricalDetail
    {
        get
        {
            if (_historical is null) return null;

            string last = $"{_historical.LastSeen.LocalDateTime:MMM d}";
            return _historical.Occurrences <= 1
                ? $"This condition is not active right now. It was last observed on {last}."
                : $"This condition is not active right now. It was observed {_historical.Occurrences} times " +
                  $"between {_historical.FirstSeen.LocalDateTime:MMM d} and {last}.";
        }
    }

    public Visibility ActionHintVisibility =>
        _insight?.ActionHint is not null ? Visibility.Visible : Visibility.Collapsed;

    // ── Chart ─────────────────────────────────────────────────────────────────

    public InsightDetailChartData? ChartData        { get; private set; }
    public Visibility              ChartVisibility   => Vis(ChartData is not null);
    public Visibility              NoChartVisibility => Vis(ChartData is null);

    // Stat row
    public string CurrentText  { get; private set; } = "—";
    public string AverageText  { get; private set; } = "—";
    public string MinText      { get; private set; } = "—";
    public string MaxText      { get; private set; } = "—";
    public string TrendText    { get; private set; } = "Stable";
    public string WindowLabel  { get; private set; } = "5 min window";

    // ── Attributed processes ──────────────────────────────────────────────────

    public IReadOnlyList<ProcessAttribution> AttributedProcesses { get; private set; } = [];
    public Visibility ProcessesVisibility =>
        AttributedProcesses.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    // ── Related timeline events ───────────────────────────────────────────────

    public IReadOnlyList<TimelineEventViewModel> RelatedEvents   { get; private set; } = [];
    public Visibility                            RelatedEventsVisibility =>
        RelatedEvents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string RelatedEventsHeader =>
        $"{RelatedEvents.Count} related event{(RelatedEvents.Count == 1 ? "" : "s")}";

    // ── Action history ────────────────────────────────────────────────────────

    public IReadOnlyList<HistoryRecordViewModel> ActionHistory    { get; private set; } = [];
    private bool                                 _historyLoading;

    public Visibility HistoryLoadingVisibility =>
        Vis(_historyLoading);
    public Visibility HistoryListVisibility    =>
        Vis(!_historyLoading && ActionHistory.Count > 0);
    public Visibility HistoryEmptyVisibility   =>
        Vis(!_historyLoading && ActionHistory.Count == 0);
    public string HistoryCountText =>
        $"{ActionHistory.Count} record{(ActionHistory.Count == 1 ? "" : "s")}";

    // ── Operational narrative ─────────────────────────────────────────────────

    public string          NarrativeHeadline   { get; private set; } = string.Empty;
    public string[]        NarrativeParagraphs { get; private set; } = [];
    public Visibility      NarrativeVisibility =>
        NarrativeParagraphs.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    // ── Confidence breakdown ──────────────────────────────────────────────────

    public string SampleCountText    { get; private set; } = string.Empty;
    public string CoverageText       { get; private set; } = string.Empty;
    public string ConfidenceRuleText { get; private set; } = string.Empty;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the workspace for the given insight ID. Call from
    /// <c>InsightDetailPage.OnNavigatedTo</c> on the UI thread.
    /// </summary>
    public InsightDetailViewModel(
        ISystemInsightEngine        intelligence,
        IOperationalNarrativeEngine narrativeEngine,
        ITelemetryHistoryBuffer     history,
        ISystemBaselineService      baseline,
        ITimelineAggregationService timeline,
        IOperationHistoryService    historyService,
        InsightHistoryRepository    insightHistory)
    {
        _intelligence    = intelligence;
        _narrativeEngine = narrativeEngine;
        _history         = history;
        _baseline        = baseline;
        _timeline        = timeline;
        _historyService  = historyService;
        _insightHistory  = insightHistory;
    }

    public void Load(string insightId)
    {
        _insightId = insightId;
        _insight   = _intelligence.CurrentInsights
                         .FirstOrDefault(i => i.Id == insightId);

        _intelligence.InsightsUpdated    += OnInsightsUpdated;
        _narrativeEngine.NarrativeUpdated += OnNarrativeUpdated;

        RefreshAll();

        _ = LoadHistoryAsync();

        // Not in the live engine — deep-linked to a resolved/historical finding
        // (e.g. from the health-score breakdown). Pull its persisted context so
        // the page shows what the finding was instead of an empty placeholder.
        if (_insight is null)
            _ = LoadHistoricalContextAsync();
    }

    /// <summary>Unsubscribes from live services. Call from Page.Unloaded.</summary>
    public void Cleanup()
    {
        _intelligence.InsightsUpdated     -= OnInsightsUpdated;
        _narrativeEngine.NarrativeUpdated -= OnNarrativeUpdated;
    }

    // ── Live update handlers ──────────────────────────────────────────────────

    private void OnInsightsUpdated(object? sender, IReadOnlyList<SystemInsight> insights)
    {
        _insight = insights.FirstOrDefault(i => i.Id == _insightId);
        RefreshAll();

        // The finding resolved while the page was open — fetch its persisted
        // context so the header keeps the real title instead of going blank.
        if (_insight is null && _historical is null)
            _ = LoadHistoricalContextAsync();
    }

    private void OnNarrativeUpdated(object? sender, OperationalNarrative narrative)
    {
        ApplyNarrative(narrative);
        OnPropertyChanged(nameof(NarrativeHeadline));
        OnPropertyChanged(nameof(NarrativeParagraphs));
        OnPropertyChanged(nameof(NarrativeVisibility));
    }

    // ── Full refresh ──────────────────────────────────────────────────────────

    private void RefreshAll()
    {
        // Relative time
        RelativeTime = _insight is not null ? FormatAge(_insight.DetectedAt)
                     : _historical is not null ? $"last seen {FormatAge(_historical.LastSeen)}"
                     : string.Empty;

        // Processes
        AttributedProcesses = _insight?.AttributedProcesses ?? [];

        // Chart + stats
        RebuildChart();

        // Related timeline events
        RebuildRelatedEvents();

        // Narrative
        var narrative = _narrativeEngine.CurrentNarrative;
        if (narrative is not null) ApplyNarrative(narrative);

        // Confidence breakdown
        BuildConfidenceReasoning();

        OnPropertyChanged(string.Empty);
    }

    // ── Chart builder ─────────────────────────────────────────────────────────

    private void RebuildChart()
    {
        var metric = TrendVisualizationHelper.MetricFor(_insightId);
        if (metric is null)
        {
            ChartData    = null;
            WindowLabel  = string.Empty;
            ClearStats();
            return;
        }

        // ── Sample collection ─────────────────────────────────────────────────
        // Window 0=1min, 1=5min, 2=15min (filtered from 30), 3=30min

        IReadOnlyList<MetricPoint> raw;
        int forecastMinutes;
        string windowLabel;

        switch (SelectedWindowIndex)
        {
            case 0:
                raw            = _history.GetSamples(metric.Value, TimeWindow.LastMinute);
                forecastMinutes = 1;
                windowLabel    = "1 min window";
                break;
            case 2:
            {
                // 15-minute window: filter from the 30-min buffer
                var all30 = _history.GetSamples(metric.Value, TimeWindow.LastThirtyMinutes);
                var cutoff = DateTimeOffset.Now - TimeSpan.FromMinutes(15);
                raw            = all30.Where(p => p.Timestamp >= cutoff).ToList();
                forecastMinutes = 6;
                windowLabel    = "15 min window";
                break;
            }
            case 3:
                raw            = _history.GetSamples(metric.Value, TimeWindow.LastThirtyMinutes);
                forecastMinutes = 10;
                windowLabel    = "30 min window";
                break;
            default: // 1 = 5 min
                raw            = _history.GetSamples(metric.Value, TimeWindow.LastFiveMinutes);
                forecastMinutes = 4;
                windowLabel    = "5 min window";
                break;
        }

        WindowLabel = windowLabel;

        if (raw.Count < 3)
        {
            ChartData = null;
            ClearStats();
            return;
        }

        // ── Downsample + project ──────────────────────────────────────────────

        var allPts = raw.Select(p => new SparklinePoint(p.Timestamp, p.Value)).ToList();
        var history  = Downsample(allPts, maxPoints: 100);
        double slope = TrendAnalysis.SlopePerMinute(raw);
        var forecast = BuildForecast(history, slope, forecastMinutes);

        // ── Baseline band ─────────────────────────────────────────────────────

        var   baseline      = _baseline.CurrentBaseline;
        double? baselineMean   = null;
        double? baselineStdDev = null;
        string unit = metric.Value == TelemetryMetric.Temperature ? "°C" : "%";

        if (baseline is not null)
        {
            (baselineMean, baselineStdDev) = metric.Value switch
            {
                TelemetryMetric.Cpu         => (baseline.IdleCpuReady
                                                    ? (double?)baseline.IdleCpuMean   : null,
                                                baseline.IdleCpuReady
                                                    ? (double?)baseline.IdleCpuStdDev : null),
                TelemetryMetric.Ram         => (baseline.NormalRamReady
                                                    ? (double?)baseline.NormalRamMean    : null,
                                                baseline.NormalRamReady
                                                    ? (double?)baseline.NormalRamStdDev  : null),
                TelemetryMetric.Temperature => (baseline.NormalThermalReady
                                                    ? (double?)baseline.NormalThermalMean   : null,
                                                baseline.NormalThermalReady
                                                    ? (double?)baseline.NormalThermalStdDev : null),
                _                           => ((double?)null, (double?)null),
            };
        }

        // ── Markers from related timeline events ──────────────────────────────

        var windowStart = raw[0].Timestamp;
        var markers = _timeline.Events
            .Where(e => e.RelatedInsightId == _insightId &&
                        e.OccurredAt >= windowStart)
            .Select(e => new ChartMarker(
                e.OccurredAt,
                e.Type is TimelineEventType.InsightOnset    ? "Onset"
                        : e.Type == TimelineEventType.InsightResolved ? "Cleared"
                        : e.Title.Length > 12 ? e.Title[..12] + "…" : e.Title,
                e.Type switch
                {
                    TimelineEventType.InsightOnset    => ChartMarkerKind.InsightOnset,
                    TimelineEventType.InsightResolved => ChartMarkerKind.InsightResolved,
                    TimelineEventType.ActionExecuted  => ChartMarkerKind.ActionExecuted,
                    TimelineEventType.ActionRollback  => ChartMarkerKind.ActionRollback,
                    _                                 => ChartMarkerKind.SessionEvent,
                }))
            .ToList();

        // ── Axis range ────────────────────────────────────────────────────────
        // For %, anchor to 0–100 for stable visual context.
        // For temperature, use a ±20°C window around the data.

        double dataMin = raw.Min(p => p.Value);
        double dataMax = raw.Max(p => p.Value);

        double axisMin, axisMax;
        if (unit == "%")
        {
            axisMin = 0;
            axisMax = 100;
        }
        else
        {
            double pad = Math.Max(5, (dataMax - dataMin) * 0.2);
            axisMin = Math.Floor(dataMin - pad);
            axisMax = Math.Ceiling(dataMax + pad);
        }

        // ── Color state ───────────────────────────────────────────────────────

        var colorState = _insightId == "system.healthy"
            ? SparklineColorState.Good
            : (_insight?.Severity ?? InsightSeverity.Info) switch
            {
                InsightSeverity.Warning        => SparklineColorState.Warning,
                InsightSeverity.Recommendation => SparklineColorState.Monitoring,
                _                              => SparklineColorState.Monitoring,
            };

        // ── Stats ─────────────────────────────────────────────────────────────

        double current = raw[^1].Value;
        double avg     = raw.Average(p => p.Value);
        double min     = raw.Min(p => p.Value);
        double max     = raw.Max(p => p.Value);

        CurrentText = $"{current:F1}{unit}";
        AverageText = $"{avg:F1}{unit}";
        MinText     = $"{min:F1}{unit}";
        MaxText     = $"{max:F1}{unit}";
        TrendText   = Math.Abs(slope) < 0.05
            ? "Stable"
            : slope > 0
                ? $"▲ +{slope:F2}{unit}/min"
                : $"▼ {slope:F2}{unit}/min";

        ChartData = new InsightDetailChartData
        {
            History        = history,
            Forecast       = forecast,
            BaselineMean   = baselineMean,
            BaselineStdDev = baselineStdDev,
            Markers        = markers,
            ColorState     = colorState,
            UnitLabel      = unit,
            MetricName     = MetricDisplayName(metric.Value),
            CurrentVal     = current,
            AverageVal     = avg,
            MinVal         = min,
            MaxVal         = max,
            SlopePerMin    = slope,
            AxisMin        = axisMin,
            AxisMax        = axisMax,
        };
    }

    private void ClearStats()
    {
        CurrentText = AverageText = MinText = MaxText = "—";
        TrendText   = string.Empty;
    }

    // ── Related events ────────────────────────────────────────────────────────

    private void RebuildRelatedEvents()
    {
        RelatedEvents = _timeline.Events
            .Where(e => e.RelatedInsightId == _insightId ||
                        (e.Type == TimelineEventType.ActionExecuted
                         && _insight?.RecommendedActions.Any(a => e.ActionId == a.Id) == true))
            .Take(20)
            .Select(e => new TimelineEventViewModel(e))
            .ToList();
    }

    // ── Async history ─────────────────────────────────────────────────────────

    private async Task LoadHistoryAsync()
    {
        _historyLoading = true;
        OnPropertyChanged(nameof(HistoryLoadingVisibility));
        OnPropertyChanged(nameof(HistoryListVisibility));  // HistoryListVisibility = !loading && Count>0 — notify on loading change
        OnPropertyChanged(nameof(HistoryEmptyVisibility));

        try
        {
            // Get up to 50 recent records, filter to those plausibly related to
            // this insight's recommended action IDs.
            var all = await _historyService
                .GetRecentAsync(50)
                .ConfigureAwait(true);

            var relatedActionIds = _insight?.RecommendedActions
                .Select(a => a.Id)
                .ToHashSet(StringComparer.Ordinal)
                ?? [];

            // Include a record when its action ID matches any of the insight's
            // recommended actions, or when no filter IDs are available (show all).
            ActionHistory = all
                .Where(r => relatedActionIds.Count == 0 || relatedActionIds.Contains(r.ActionId))
                .Select(r => new HistoryRecordViewModel(r))
                .ToList();
        }
        catch
        {
            ActionHistory = [];
        }
        finally
        {
            _historyLoading = false;
            OnPropertyChanged(nameof(HistoryLoadingVisibility));
            OnPropertyChanged(nameof(HistoryListVisibility));
            OnPropertyChanged(nameof(HistoryEmptyVisibility));
            OnPropertyChanged(nameof(HistoryCountText));
            OnPropertyChanged(nameof(ActionHistory));
        }
    }

    // ── Historical fallback ───────────────────────────────────────────────────

    /// <summary>
    /// Loads persisted onset/resolution rows for a finding that is not in the
    /// live engine and condenses them into one <see cref="HealthScoreContribution"/>
    /// (same grouping the health-score breakdown uses, so the two pages agree).
    /// Looks at the score's 7-day window first, widening to 30 days for links
    /// from longer-horizon views. Best-effort: on failure the page keeps the
    /// generic cleared state.
    /// </summary>
    private async Task LoadHistoricalContextAsync()
    {
        try
        {
            var now  = DateTimeOffset.UtcNow;
            var rows = await _insightHistory
                .GetHistoryForInsightAsync(_insightId, now.AddDays(-7), now)
                .ConfigureAwait(true);

            if (rows.Count == 0)
                rows = await _insightHistory
                    .GetHistoryForInsightAsync(_insightId, now.AddDays(-30), now)
                    .ConfigureAwait(true);

            _historical = HealthScoreCalculator.BuildContributions(rows).FirstOrDefault();
        }
        catch
        {
            _historical = null;   // history is context, not critical — keep the generic cleared state
        }

        // Only repaint if the finding is still inactive; a live insight that
        // reappeared meanwhile already owns the header.
        if (_insight is null && _historical is not null)
            RefreshAll();
    }

    // ── Narrative ─────────────────────────────────────────────────────────────

    private void ApplyNarrative(OperationalNarrative n)
    {
        NarrativeHeadline   = n.Headline;
        NarrativeParagraphs = n.Paragraphs;
    }

    // ── Confidence breakdown ──────────────────────────────────────────────────

    private void BuildConfidenceReasoning()
    {
        if (_insight is null)
        {
            SampleCountText    = string.Empty;
            CoverageText       = string.Empty;
            ConfidenceRuleText = string.Empty;
            return;
        }

        // Try to get sample count for confidence context.
        var metric = TrendVisualizationHelper.MetricFor(_insightId);
        if (metric is not null)
        {
            var stats    = _history.GetStats(metric.Value, TimeWindow.LastFiveMinutes);
            int expected = (int)(300 / 1.5);   // 5 min ÷ 1.5s per tick ≈ 200 samples
            int coverage = stats.SampleCount == 0 ? 0
                         : (int)Math.Min(100, stats.SampleCount * 100.0 / expected);

            SampleCountText = $"{stats.SampleCount} samples";
            CoverageText    = $"{coverage}% of 5-min window";
        }
        else
        {
            SampleCountText = "point-in-time reading";
            CoverageText    = "direct sensor measurement";
        }

        ConfidenceRuleText = _insightId.Contains("baseline")  ? "Baseline anomaly — deviation from machine-specific normal range."
                           : _insightId.Contains("forecast")  ? "Linear extrapolation — confidence decreases with forecast horizon."
                           : _insightId.Contains("synthesis") ? "Cross-insight correlation — requires multiple concurrent findings."
                           : _insightId.Contains("trend")
                             || _insightId.Contains("escalat")
                             || _insightId.Contains("rising")  ? "Trend rule — requires sustained history; scales with window coverage."
                                                               : "Threshold rule — fires when current values cross a defined limit.";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<SparklinePoint> Downsample(
        List<SparklinePoint> pts, int maxPoints)
    {
        if (pts.Count <= maxPoints) return pts;

        var result = new List<SparklinePoint>(maxPoints);
        double step = (double)(pts.Count - 1) / (maxPoints - 1);
        for (int i = 0; i < maxPoints; i++)
        {
            int idx = (int)Math.Round(i * step);
            result.Add(pts[Math.Min(idx, pts.Count - 1)]);
        }
        return result;
    }

    private static IReadOnlyList<SparklinePoint> BuildForecast(
        IReadOnlyList<SparklinePoint> history,
        double slopePerMinute,
        int forecastMinutes)
    {
        if (history.Count == 0 || Math.Abs(slopePerMinute) < 0.05) return [];

        var last  = history[^1];
        int steps = forecastMinutes * 4;
        var result = new List<SparklinePoint>(steps + 1) { last };

        for (int i = 1; i <= steps; i++)
        {
            double min  = i / 4.0;
            double proj = Math.Clamp(last.Value + slopePerMinute * min, 0, 200);
            result.Add(new SparklinePoint(last.Timestamp.AddSeconds(i * 15), proj));
        }

        return result;
    }

    private static string FormatAge(DateTimeOffset t)
    {
        var age = DateTimeOffset.Now - t;
        if (age.TotalSeconds < 30) return "just now";
        if (age.TotalMinutes  < 1) return $"{(int)age.TotalSeconds}s ago";
        if (age.TotalMinutes  < 60) return $"{(int)age.TotalMinutes} min ago";
        if (age.TotalHours    < 48) return $"{(int)age.TotalHours}h ago";
        return $"{(int)age.TotalDays} days ago";
    }

    private static string MetricDisplayName(TelemetryMetric m) => m switch
    {
        TelemetryMetric.Cpu         => "CPU Usage",
        TelemetryMetric.Ram         => "RAM Usage",
        TelemetryMetric.Gpu         => "GPU Usage",
        TelemetryMetric.Disk        => "Disk Activity",
        TelemetryMetric.Temperature => "CPU Temperature",
        _                           => "Metric",
    };

    private static Visibility Vis(bool v) => v ? Visibility.Visible : Visibility.Collapsed;

    private static SolidColorBrush Accent(InsightSeverity s, string id) => s switch
    {
        InsightSeverity.Warning        => s_warningAccent,
        InsightSeverity.Recommendation => s_infoAccent,
        _ => id == "system.healthy"    ? s_goodAccent : s_infoAccent,
    };

    private static SolidColorBrush Bg(InsightSeverity s, string id) => s switch
    {
        InsightSeverity.Warning        => s_warningBg,
        InsightSeverity.Recommendation => s_infoBg,
        _ => id == "system.healthy"    ? s_goodBg : s_infoBg,
    };
}
