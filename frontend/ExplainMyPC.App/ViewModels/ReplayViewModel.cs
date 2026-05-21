using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplainMyPC.Services.History;
using ExplainMyPC.Services.Replay;
using ExplainMyPC.Services.Timeline;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ExplainMyPC.ViewModels;

// ── Sub-ViewModels ─────────────────────────────────────────────────────────────
// Simple init-only data holders used by ItemsControl DataTemplates.
// The parent ViewModel clears and repopulates each collection wholesale on each
// state update, so per-item INotifyPropertyChanged is not needed.

public sealed class EventMarkerViewModel
{
    public string Title         { get; init; } = "";
    public string GlyphText     { get; init; } = "";
    public string TimeText      { get; init; } = "";
    public Brush  AccentBrush   { get; init; } = new SolidColorBrush(Colors.Gray);
    public double OffsetSeconds { get; init; }
}

public sealed class ActiveInsightViewModel
{
    public string     Title               { get; init; } = "";
    public string     SeverityGlyph       { get; init; } = "";
    public Brush      SeverityBrush       { get; init; } = new SolidColorBrush(Colors.Gray);
    public string     SeverityLabel       { get; init; } = "";
    public string     DurationText        { get; init; } = "";
    public string     ProcessText         { get; init; } = "";
    public Visibility ProcessesVisibility { get; init; }
}

public sealed class DeltaItemViewModel
{
    public string Label      { get; init; } = "";
    public string Glyph      { get; init; } = "";
    public Brush  GlyphBrush { get; init; } = new SolidColorBrush(Colors.Gray);
}

public sealed class CausalLinkViewModel
{
    public string Title          { get; init; } = "";
    public string Description    { get; init; } = "";
    public string TimeText       { get; init; } = "";
    public string ConfidenceText { get; init; } = "";
    public string Glyph          { get; init; } = "";
    public Brush  GlyphBrush     { get; init; } = new SolidColorBrush(Colors.Gray);
}

public sealed class ComparisonMetricViewModel
{
    public string Label      { get; init; } = "";
    public string BeforeText { get; init; } = "";
    public string AfterText  { get; init; } = "";
    public string DeltaText  { get; init; } = "";
    public Brush  DeltaBrush { get; init; } = new SolidColorBrush(Colors.Gray);
}

// ── Main ViewModel ─────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for the Operational Replay workspace.
///
/// Orchestrates session creation, cursor-based state reconstruction, delta
/// analysis, causal chain building, and narrative composition. All derived
/// state flows from a single UpdateStateAtCursor() call triggered by slider changes.
///
/// Threading:
///   LoadSessionAsync() is called fire-and-forget from the constructor (UI thread).
///   UpdateStateAtCursor() is synchronous and always called on the UI thread.
/// </summary>
public sealed partial class ReplayViewModel : ObservableObject
{
    private readonly IOperationalReplayService _replay;
    private ReplaySession? _session;

    // Guards against re-entrant cursor updates while we set CursorSeconds
    // programmatically during session load.
    private bool _isUpdatingCursor;

    private ReplayWindow _selectedWindow = ReplayWindow.FullSession;

    // ── Page state ────────────────────────────────────────────────────────────

    [ObservableProperty] private Visibility _loadingVisibility = Visibility.Visible;
    [ObservableProperty] private Visibility _sessionVisibility = Visibility.Collapsed;
    [ObservableProperty] private Visibility _noDataVisibility  = Visibility.Collapsed;

    // ── Session info bar ──────────────────────────────────────────────────────

    [ObservableProperty] private string _sessionCoverage  = "";
    [ObservableProperty] private string _dataPointsText   = "";
    [ObservableProperty] private string _healthStateLabel = "—";

    // ── Cursor ────────────────────────────────────────────────────────────────

    [ObservableProperty] private double _totalSessionSeconds = 1;
    [ObservableProperty] private double _cursorSeconds;
    [ObservableProperty] private string _cursorTimeText = "";

    /// <summary>Fires every time the user moves the slider. Triggers state reconstruction.</summary>
    partial void OnCursorSecondsChanged(double value)
    {
        if (_session is null || _isUpdatingCursor) return;
        var targetTime = _session.OldestDataPoint.AddSeconds(value);
        UpdateStateAtCursor(targetTime);
    }

    // ── Metrics at cursor ─────────────────────────────────────────────────────

    [ObservableProperty] private double     _cpuAtCursor;
    [ObservableProperty] private double     _ramAtCursor;
    [ObservableProperty] private double     _gpuAtCursor;
    [ObservableProperty] private double     _diskAtCursor;
    [ObservableProperty] private double     _tempAtCursor;
    [ObservableProperty] private Visibility _tempRowVisibility = Visibility.Collapsed;
    [ObservableProperty] private string     _samplesText       = "";

    // Pre-formatted metric text (WinUI 3 does not support StringFormat on {Binding})
    [ObservableProperty] private string _cpuText  = "—";
    [ObservableProperty] private string _ramText  = "—";
    [ObservableProperty] private string _gpuText  = "—";
    [ObservableProperty] private string _diskText = "—";
    [ObservableProperty] private string _tempText = "—";

    // ── Section visibility ────────────────────────────────────────────────────

    [ObservableProperty] private Visibility _noInsightsVisibility      = Visibility.Visible;
    [ObservableProperty] private Visibility _hasInsightsVisibility     = Visibility.Collapsed;
    [ObservableProperty] private Visibility _noDeltaVisibility         = Visibility.Visible;
    [ObservableProperty] private Visibility _hasDeltaVisibility        = Visibility.Collapsed;
    [ObservableProperty] private Visibility _noCausalLinksVisibility   = Visibility.Visible;
    [ObservableProperty] private Visibility _hasCausalLinksVisibility  = Visibility.Collapsed;
    [ObservableProperty] private Visibility _noEventMarkersVisibility  = Visibility.Visible;
    [ObservableProperty] private Visibility _hasEventMarkersVisibility = Visibility.Collapsed;
    [ObservableProperty] private Visibility _comparisonVisibility      = Visibility.Collapsed;

    // ── Before/After comparison ───────────────────────────────────────────────

    [ObservableProperty] private string     _comparisonTitle          = "";
    [ObservableProperty] private string     _comparisonNarrative      = "";
    [ObservableProperty] private string     _comparisonOutcomeText    = "";
    [ObservableProperty] private Visibility _improvementVisibility    = Visibility.Collapsed;
    [ObservableProperty] private Visibility _noImprovementVisibility  = Visibility.Collapsed;

    // ── Narrative paragraphs ──────────────────────────────────────────────────

    [ObservableProperty] private string _narrativeParagraph1 = "";
    [ObservableProperty] private string _narrativeParagraph2 = "";
    [ObservableProperty] private string _narrativeParagraph3 = "";
    [ObservableProperty] private string _narrativeParagraph4 = "";
    [ObservableProperty] private string _narrativeParagraph2Visibility = "";  // unused — managed below
    [ObservableProperty] private Visibility _p2Visibility = Visibility.Collapsed;
    [ObservableProperty] private Visibility _p3Visibility = Visibility.Collapsed;
    [ObservableProperty] private Visibility _p4Visibility = Visibility.Collapsed;

    // ── Window selection chips ────────────────────────────────────────────────

    [ObservableProperty] private bool _isWindowFull = true;
    [ObservableProperty] private bool _isWindowHour;
    [ObservableProperty] private bool _isWindow15m;
    [ObservableProperty] private bool _isWindow5m;

    // ── Collections ───────────────────────────────────────────────────────────

    public ObservableCollection<EventMarkerViewModel>      EventMarkers      { get; } = new();
    public ObservableCollection<ActiveInsightViewModel>    ActiveInsights    { get; } = new();
    public ObservableCollection<DeltaItemViewModel>        DeltaItems        { get; } = new();
    public ObservableCollection<CausalLinkViewModel>       CausalLinks       { get; } = new();
    public ObservableCollection<ComparisonMetricViewModel> ComparisonMetrics { get; } = new();

    // ── Commands ──────────────────────────────────────────────────────────────

    public IAsyncRelayCommand RefreshCommand          { get; }
    public IRelayCommand      SeekToBeginCommand      { get; }
    public IRelayCommand      SeekToEndCommand        { get; }
    public IRelayCommand      SelectWindowFullCommand  { get; }
    public IRelayCommand      SelectWindowHourCommand  { get; }
    public IRelayCommand      SelectWindow15mCommand   { get; }
    public IRelayCommand      SelectWindow5mCommand    { get; }

    // ── Construction ──────────────────────────────────────────────────────────

    public ReplayViewModel(IOperationalReplayService replay)
    {
        _replay = replay;

        RefreshCommand          = new AsyncRelayCommand(LoadSessionAsync);
        SeekToBeginCommand      = new RelayCommand(() => CursorSeconds = 0);
        SeekToEndCommand        = new RelayCommand(() => CursorSeconds = TotalSessionSeconds);
        SelectWindowFullCommand = new RelayCommand(() => SelectWindow(ReplayWindow.FullSession));
        SelectWindowHourCommand = new RelayCommand(() => SelectWindow(ReplayWindow.LastHour));
        SelectWindow15mCommand  = new RelayCommand(() => SelectWindow(ReplayWindow.Last15Minutes));
        SelectWindow5mCommand   = new RelayCommand(() => SelectWindow(ReplayWindow.Last5Minutes));

        // Fire-and-forget load on construction — safe because this always
        // runs on the UI thread and UpdateStateAtCursor is synchronous.
        _ = LoadSessionAsync();
    }

    // ── Session loading ───────────────────────────────────────────────────────

    private async Task LoadSessionAsync()
    {
        LoadingVisibility = Visibility.Visible;
        SessionVisibility = Visibility.Collapsed;
        NoDataVisibility  = Visibility.Collapsed;

        try
        {
            _session = await _replay.CreateSessionAsync(_selectedWindow);

            bool hasData = _session.HasTelemetry || _session.HasEvents || _session.HasActions;
            if (!hasData)
            {
                NoDataVisibility = Visibility.Visible;
                return;
            }

            // Session time range
            TotalSessionSeconds = Math.Max(_session.Coverage.TotalSeconds, 1);
            SessionCoverage     = FormatCoverage(_session.Coverage);
            DataPointsText      = $"{_session.TotalDataPoints:N0} data points";

            RebuildEventMarkers();

            // Default: seek to the present (right edge of scrubber)
            _isUpdatingCursor = true;
            CursorSeconds     = TotalSessionSeconds;
            _isUpdatingCursor = false;

            UpdateStateAtCursor(_session.NewestDataPoint);

            SessionVisibility = Visibility.Visible;
        }
        catch
        {
            NoDataVisibility = Visibility.Visible;
        }
        finally
        {
            LoadingVisibility = Visibility.Collapsed;
        }
    }

    // ── State reconstruction ──────────────────────────────────────────────────

    private void UpdateStateAtCursor(DateTimeOffset targetTime)
    {
        if (_session is null) return;

        CursorTimeText = targetTime.ToString("h:mm:ss tt");

        var snapshot = _replay.ReconstructStateAt(_session, targetTime);
        if (snapshot is null) return;

        // Metrics
        CpuAtCursor       = snapshot.Telemetry.CpuPercent;
        RamAtCursor       = snapshot.Telemetry.RamPercent;
        GpuAtCursor       = snapshot.Telemetry.GpuPercent;
        DiskAtCursor      = snapshot.Telemetry.DiskPercent;
        TempAtCursor      = snapshot.Telemetry.TemperatureCelsius;
        TempRowVisibility = snapshot.Telemetry.HasTemperature ? Visibility.Visible : Visibility.Collapsed;
        SamplesText       = $"{snapshot.DataPointsAvailable:N0} samples";

        CpuText  = $"{snapshot.Telemetry.CpuPercent:F0}%";
        RamText  = $"{snapshot.Telemetry.RamPercent:F0}%";
        GpuText  = $"{snapshot.Telemetry.GpuPercent:F0}%";
        DiskText = $"{snapshot.Telemetry.DiskPercent:F0}%";
        TempText = snapshot.Telemetry.HasTemperature
            ? $"{snapshot.Telemetry.TemperatureCelsius:F0}°C"
            : "—";

        HealthStateLabel = snapshot.HealthStateLabel;

        // Active insights at cursor
        ActiveInsights.Clear();
        foreach (var insight in snapshot.ActiveInsights)
            ActiveInsights.Add(MapInsight(insight));

        NoInsightsVisibility  = snapshot.ActiveInsights.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HasInsightsVisibility = snapshot.ActiveInsights.Count >  0 ? Visibility.Visible : Visibility.Collapsed;

        // Delta: from session start to current cursor position
        var origin = _replay.ReconstructStateAt(_session, _session.OldestDataPoint);
        if (origin is not null)
            RebuildDeltaView(_replay.ComputeDelta(origin, snapshot));

        // Causal chain across the full visible window up to cursor
        RebuildCausalView(_replay.BuildCausalChain(_session, _session.OldestDataPoint, targetTime));

        // Before/After comparison — shown only when cursor is near a completed action
        var nearestAction = _replay.FindNearestAction(_session, targetTime);
        if (nearestAction is not null)
        {
            var comparison = _replay.BuildRemediationComparison(_session, nearestAction);
            if (comparison is not null)
            {
                RebuildComparisonView(comparison);
                ComparisonVisibility = Visibility.Visible;
            }
            else
            {
                ComparisonVisibility = Visibility.Collapsed;
            }
        }
        else
        {
            ComparisonVisibility = Visibility.Collapsed;
        }

        // Narrative
        var narrative = _replay.ComposeNarrative(_session, targetTime);
        var paras     = narrative.Paragraphs;
        NarrativeParagraph1 = paras.Length > 0 ? paras[0] : "";
        NarrativeParagraph2 = paras.Length > 1 ? paras[1] : "";
        NarrativeParagraph3 = paras.Length > 2 ? paras[2] : "";
        NarrativeParagraph4 = paras.Length > 3 ? paras[3] : "";
        P2Visibility = paras.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        P3Visibility = paras.Length > 2 ? Visibility.Visible : Visibility.Collapsed;
        P4Visibility = paras.Length > 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Collection rebuilds ───────────────────────────────────────────────────

    private void RebuildEventMarkers()
    {
        if (_session is null) return;
        EventMarkers.Clear();

        foreach (var ev in _session.Events)
        {
            EventMarkers.Add(new EventMarkerViewModel
            {
                Title         = ev.Title,
                GlyphText     = EventGlyph(ev.Type),
                TimeText      = ev.OccurredAt.ToString("h:mm tt"),
                AccentBrush   = SeverityToBrush(ev.Severity),
                OffsetSeconds = (ev.OccurredAt - _session.OldestDataPoint).TotalSeconds,
            });
        }

        foreach (var action in _session.Actions.Where(a => a.IsSuccess && !a.IsDryRun))
        {
            EventMarkers.Add(new EventMarkerViewModel
            {
                Title         = action.ActionTitle,
                GlyphText     = "",
                TimeText      = action.ExecutedAt.ToString("h:mm tt"),
                AccentBrush   = new SolidColorBrush(Color.FromArgb(255, 78, 161, 255)),
                OffsetSeconds = (action.ExecutedAt - _session.OldestDataPoint).TotalSeconds,
            });
        }

        // Sort chronologically
        var sorted = EventMarkers.OrderBy(m => m.OffsetSeconds).ToList();
        EventMarkers.Clear();
        foreach (var m in sorted) EventMarkers.Add(m);

        NoEventMarkersVisibility  = EventMarkers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HasEventMarkersVisibility = EventMarkers.Count >  0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RebuildDeltaView(OperationalDelta delta)
    {
        DeltaItems.Clear();
        foreach (var line in delta.SummaryLines.Take(6))
        {
            bool improving = line.StartsWith("CPU usage decreased")     ||
                             line.StartsWith("RAM usage dropped")        ||
                             line.StartsWith("CPU temperature dropped")  ||
                             line.StartsWith("Disk activity decreased")  ||
                             line.StartsWith("Resolved:");

            DeltaItems.Add(new DeltaItemViewModel
            {
                Label      = line,
                Glyph      = improving ? "" : "",
                GlyphBrush = improving
                    ? new SolidColorBrush(Color.FromArgb(255, 87,  214, 141))
                    : new SolidColorBrush(Color.FromArgb(255, 255, 179, 71)),
            });
        }

        NoDeltaVisibility  = DeltaItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HasDeltaVisibility = DeltaItems.Count >  0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RebuildCausalView(CausalChain chain)
    {
        CausalLinks.Clear();
        foreach (var link in chain.Links)
        {
            CausalLinks.Add(new CausalLinkViewModel
            {
                Title          = link.EventTitle,
                Description    = link.Description,
                TimeText       = link.OccurredAt.ToString("h:mm:ss"),
                ConfidenceText = $"{link.ConfidencePercent}% confidence",
                Glyph          = CausalKindGlyph(link.Kind),
                GlyphBrush     = CausalKindBrush(link.Kind),
            });
        }

        NoCausalLinksVisibility  = CausalLinks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HasCausalLinksVisibility = CausalLinks.Count >  0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RebuildComparisonView(RemediationComparison comparison)
    {
        ComparisonTitle    = $"Before / After:  {comparison.Action.ActionTitle}";
        ComparisonNarrative = comparison.ComparisonNarrative;
        ComparisonOutcomeText = comparison.ShowsImprovement
            ? "Positive effect observed in comparison window"
            : "Effect not clearly measurable in comparison window";

        ImprovementVisibility   = comparison.ShowsImprovement ? Visibility.Visible  : Visibility.Collapsed;
        NoImprovementVisibility = comparison.ShowsImprovement ? Visibility.Collapsed : Visibility.Visible;

        ComparisonMetrics.Clear();
        AddComparisonMetric("CPU",  comparison.SnapshotBefore.Telemetry.CpuPercent,  comparison.SnapshotAfter.Telemetry.CpuPercent,  "%");
        AddComparisonMetric("RAM",  comparison.SnapshotBefore.Telemetry.RamPercent,  comparison.SnapshotAfter.Telemetry.RamPercent,  "%");
        AddComparisonMetric("Disk", comparison.SnapshotBefore.Telemetry.DiskPercent, comparison.SnapshotAfter.Telemetry.DiskPercent, "%");

        if (comparison.SnapshotBefore.Telemetry.HasTemperature &&
            comparison.SnapshotAfter.Telemetry.HasTemperature)
        {
            AddComparisonMetric("Temp",
                comparison.SnapshotBefore.Telemetry.TemperatureCelsius,
                comparison.SnapshotAfter.Telemetry.TemperatureCelsius, "°C");
        }
    }

    private void AddComparisonMetric(string label, double before, double after, string unit)
    {
        double delta    = after - before;
        bool   improving = delta < 0;      // lower is better for all hardware metrics
        string deltaStr = delta >= 0 ? $"+{delta:F0}{unit}" : $"{delta:F0}{unit}";

        ComparisonMetrics.Add(new ComparisonMetricViewModel
        {
            Label      = label,
            BeforeText = $"{before:F0}{unit}",
            AfterText  = $"{after:F0}{unit}",
            DeltaText  = deltaStr,
            DeltaBrush = improving
                ? new SolidColorBrush(Color.FromArgb(255, 87,  214, 141))
                : new SolidColorBrush(Color.FromArgb(255, 255, 179, 71)),
        });
    }

    private void SelectWindow(ReplayWindow window)
    {
        _selectedWindow  = window;
        IsWindowFull     = window == ReplayWindow.FullSession;
        IsWindowHour     = window == ReplayWindow.LastHour;
        IsWindow15m      = window == ReplayWindow.Last15Minutes;
        IsWindow5m       = window == ReplayWindow.Last5Minutes;
        _ = LoadSessionAsync();
    }

    /// <summary>
    /// Seeks the cursor to the given offset, then reconstructs state.
    /// Called from code-behind when the user clicks an event marker chip.
    /// </summary>
    public void SeekToOffset(double offsetSeconds)
    {
        if (_session is null) return;
        _isUpdatingCursor = true;
        CursorSeconds     = Math.Clamp(offsetSeconds, 0, TotalSessionSeconds);
        _isUpdatingCursor = false;
        var targetTime = _session.OldestDataPoint.AddSeconds(CursorSeconds);
        UpdateStateAtCursor(targetTime);
    }

    // ── Static mapping helpers ────────────────────────────────────────────────

    private static ActiveInsightViewModel MapInsight(ActiveInsightInfo insight)
    {
        var (glyph, brush) = SeverityGlyphAndBrush(insight.Severity);

        var age          = DateTimeOffset.Now - insight.OnsetAt;
        var durationText = age.TotalSeconds < 90
            ? "Just now"
            : age.TotalMinutes < 60
                ? $"{(int)age.TotalMinutes}m active"
                : $"{(int)age.TotalHours}h {age.Minutes}m active";

        return new ActiveInsightViewModel
        {
            Title               = insight.Title,
            SeverityGlyph       = glyph,
            SeverityBrush       = brush,
            SeverityLabel       = insight.Severity.ToString(),
            DurationText        = durationText,
            ProcessText         = insight.AttributedProcesses.Count > 0
                                    ? string.Join(", ", insight.AttributedProcesses.Take(3))
                                    : "",
            ProcessesVisibility = insight.AttributedProcesses.Count > 0
                                    ? Visibility.Visible
                                    : Visibility.Collapsed,
        };
    }

    private static (string glyph, Brush brush) SeverityGlyphAndBrush(TimelineEventSeverity severity)
        => severity switch
        {
            TimelineEventSeverity.Critical => ("", new SolidColorBrush(Color.FromArgb(255, 232,  64,  64))),
            TimelineEventSeverity.Warning  => ("", new SolidColorBrush(Color.FromArgb(255, 255, 179,  71))),
            TimelineEventSeverity.Info     => ("", new SolidColorBrush(Color.FromArgb(255,  78, 161, 255))),
            _                              => ("", new SolidColorBrush(Color.FromArgb(255, 170, 178, 192))),
        };

    private static Brush SeverityToBrush(TimelineEventSeverity severity)
        => severity switch
        {
            TimelineEventSeverity.Critical => new SolidColorBrush(Color.FromArgb(255, 232,  64,  64)),
            TimelineEventSeverity.Warning  => new SolidColorBrush(Color.FromArgb(255, 255, 179,  71)),
            TimelineEventSeverity.Info     => new SolidColorBrush(Color.FromArgb(255,  78, 161, 255)),
            _                              => new SolidColorBrush(Color.FromArgb(255, 170, 178, 192)),
        };

    private static string EventGlyph(TimelineEventType type)
        => type switch
        {
            TimelineEventType.InsightOnset        => "",
            TimelineEventType.InsightResolved     => "",
            TimelineEventType.ForecastAlert       => "",
            TimelineEventType.ActionExecuted      => "",
            TimelineEventType.SynthesisCorrelated => "",
            TimelineEventType.ProcessAnomaly      => "",
            _                                     => "",
        };

    private static string CausalKindGlyph(CausalLinkKind kind)
        => kind switch
        {
            CausalLinkKind.TelemetryShift       => "",
            CausalLinkKind.InsightOnset         => "",
            CausalLinkKind.ProcessEscalation    => "",
            CausalLinkKind.ForecastEscalation   => "",
            CausalLinkKind.ActionTaken          => "",
            CausalLinkKind.Stabilization        => "",
            CausalLinkKind.SynthesisCorrelation => "",
            _                                   => "",
        };

    private static Brush CausalKindBrush(CausalLinkKind kind)
        => kind switch
        {
            CausalLinkKind.ActionTaken   => new SolidColorBrush(Color.FromArgb(255,  78, 161, 255)),
            CausalLinkKind.Stabilization => new SolidColorBrush(Color.FromArgb(255,  87, 214, 141)),
            CausalLinkKind.InsightOnset  => new SolidColorBrush(Color.FromArgb(255, 255, 179,  71)),
            _                            => new SolidColorBrush(Color.FromArgb(255, 120, 120, 140)),
        };

    private static string FormatCoverage(TimeSpan t)
    {
        if (t.TotalSeconds < 90) return $"{(int)t.TotalSeconds} seconds";
        if (t.TotalMinutes < 90) return $"{(int)t.TotalMinutes} minutes";
        if (t.TotalHours   < 24) return $"{t.Hours}h {t.Minutes}m";
        return $"{(int)t.TotalDays}d";
    }
}
