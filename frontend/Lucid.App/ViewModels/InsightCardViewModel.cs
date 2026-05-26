using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Intelligence;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lucid.ViewModels;

/// <summary>
/// Display-layer wrapper for a <see cref="SystemInsight"/> produced by the
/// intelligence engine.
///
/// Translates the data model — InsightSeverity, rule ID, confidence % —
/// into ready-to-bind view properties (brushes, glyphs, Visibility tokens,
/// relative timestamps) so DataTemplates need no converters.
///
/// Static brush palette:
///   One SolidColorBrush per severity tier is allocated at class init time
///   and shared across every card. Zero allocations per telemetry tick.
///
/// Updating:
///   Call <see cref="Update"/> when the engine republishes the same finding
///   with a refreshed Detail string. OnPropertyChanged("") triggers all
///   OneWay bindings to re-read from the new backing insight while leaving
///   <see cref="IsExpanded"/> untouched.
///
/// Expandability:
///   <see cref="IsExpanded"/> drives <see cref="ExpandedVisibility"/> and
///   <see cref="ExpandChevron"/>. Toggled via <see cref="ToggleExpandedCommand"/>.
/// </summary>
public sealed partial class InsightCardViewModel : ObservableObject
{
    // ── Static brush palette ── one allocation per tier, ever ─────────────────

    private static readonly SolidColorBrush s_warningAccent = Mk(255, 255, 179,  71); // #FFB347
    private static readonly SolidColorBrush s_infoAccent    = Mk(255,  78, 161, 255); // #4EA1FF
    private static readonly SolidColorBrush s_goodAccent    = Mk(255,  87, 214, 141); // #57D68D

    // Container / badge backgrounds — 38 % alpha tint
    private static readonly SolidColorBrush s_warningBg = Mk(0x26, 255, 179,  71);
    private static readonly SolidColorBrush s_infoBg    = Mk(0x26,  78, 161, 255);
    private static readonly SolidColorBrush s_goodBg    = Mk(0x26,  87, 214, 141);

    // Action-hint chip backgrounds — 26 % alpha tint
    private static readonly SolidColorBrush s_warningSubtle = Mk(0x1A, 255, 179,  71);
    private static readonly SolidColorBrush s_infoSubtle    = Mk(0x1A,  78, 161, 255);
    private static readonly SolidColorBrush s_goodSubtle    = Mk(0x1A,  87, 214, 141);

    private static SolidColorBrush Mk(byte a, byte r, byte g, byte b) =>
        new(Color.FromArgb(a, r, g, b));

    // ── Backing insight + derived actions ────────────────────────────────────

    private SystemInsight                   _insight;
    private IReadOnlyList<ActionCardViewModel> _actions;

    // ── Local view state ──────────────────────────────────────────────────────

    /// <summary>
    /// Controls whether the full detail panel is visible.
    /// Preserved across engine re-evaluations — Update() does not touch it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandedVisibility))]
    [NotifyPropertyChangedFor(nameof(ExpandChevron))]
    private bool _isExpanded;

    // ── Construction + updating ───────────────────────────────────────────────

    public InsightCardViewModel(SystemInsight insight)
    {
        _insight      = insight;
        _actions      = BuildActions();
        SparklineData = TrendVisualizationHelper.Build(_insight.Id, _insight.Severity);
    }

    /// <summary>
    /// Replaces the backing insight with refreshed data from the engine.
    /// Fires <see cref="ObservableObject.OnPropertyChanged(string)"/> with
    /// an empty string to refresh all OneWay bindings without disturbing
    /// <see cref="IsExpanded"/>.
    /// </summary>
    public void Update(SystemInsight insight)
    {
        _insight      = insight;
        _actions      = BuildActions();
        SparklineData = TrendVisualizationHelper.Build(_insight.Id, _insight.Severity);
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Wraps each recommended action in a display-layer view model.</summary>
    private IReadOnlyList<ActionCardViewModel> BuildActions() =>
        _insight.RecommendedActions.Count == 0
            ? []
            : _insight.RecommendedActions
                      .Select(a => new ActionCardViewModel(a))
                      .ToList();

    // ── Identity / grouping ───────────────────────────────────────────────────

    public string          Id       => _insight.Id;
    public InsightSeverity Severity => _insight.Severity;

    /// <summary>Stable secondary sort key (alphabetical by rule ID).</summary>
    public string SortKey => _insight.Id;

    // ── Content ───────────────────────────────────────────────────────────────

    public string         Title             => _insight.Title;
    public string         Detail            => _insight.Detail;
    public string?        ActionHint        => _insight.ActionHint;
    public int            ConfidencePercent => _insight.ConfidencePercent;
    public DateTimeOffset DetectedAt        => _insight.DetectedAt;

    /// <summary>
    /// First sentence of the detail text (up to 90 chars) for the collapsed
    /// card header — enough context without the full paragraph.
    /// </summary>
    public string DetailPreview
    {
        get
        {
            var d   = _insight.Detail;
            int dot = d.IndexOf('.');
            if (dot > 0 && dot < 90)
                return d[..(dot + 1)];
            return d.Length <= 90 ? d : d[..87] + "…";
        }
    }

    /// <summary>Human-readable confidence, e.g. "94%".</summary>
    public string ConfidenceText => $"{_insight.ConfidencePercent}%";

    /// <summary>
    /// Human-readable age of the finding: "just now", "45s ago", "3 min ago".
    /// Refreshed by the engine's Update() call when a finding reappears.
    /// </summary>
    public string RelativeTime
    {
        get
        {
            var age = DateTimeOffset.Now - _insight.DetectedAt;
            if (age.TotalSeconds < 30)  return "just now";
            if (age.TotalMinutes  < 1)  return $"{(int)age.TotalSeconds}s ago";
            if (age.TotalMinutes  < 60) return $"{(int)age.TotalMinutes} min ago";
            return $"{(int)age.TotalHours}h ago";
        }
    }

    // ── Severity-driven visuals ───────────────────────────────────────────────

    /// <summary>Foreground / accent colour for the icon, badge text, and hint text.</summary>
    public SolidColorBrush AccentBrush             => Accent(_insight.Severity, _insight.Id);

    /// <summary>Background tint for the 40 × 40 icon container.</summary>
    public SolidColorBrush IconContainerBackground => Bg(_insight.Severity, _insight.Id);

    /// <summary>Background tint for the severity badge pill.</summary>
    public SolidColorBrush BadgeBackground         => Bg(_insight.Severity, _insight.Id);

    /// <summary>Very subtle tint background for the action-hint chip.</summary>
    public SolidColorBrush ActionHintBackground    => Subtle(_insight.Severity, _insight.Id);

    // ── Correlation detection ─────────────────────────────────────────────────

    /// <summary>
    /// True when this insight is a synthesized cross-insight correlation.
    /// Synthesis insight IDs always begin with "synthesis." — this is the
    /// only detection mechanism used throughout the codebase.
    /// </summary>
    public bool IsCorrelated =>
        _insight.Id.StartsWith("synthesis.", StringComparison.Ordinal);

    /// <summary>
    /// Controls visibility of the small "Correlated" chip shown in the card
    /// header alongside the severity badge — only for synthesis insights.
    /// </summary>
    public Visibility CorrelatedTagVisibility =>
        IsCorrelated ? Visibility.Visible : Visibility.Collapsed;

    // ── Trend detection ───────────────────────────────────────────────────────

    /// <summary>
    /// Rule IDs that belong to the temporal-trend analysis layer.
    /// These rules require ≥2–8 min of history and use linear regression
    /// or window-delta analysis rather than simple threshold checks.
    /// </summary>
    private static readonly HashSet<string> s_trendIds = new(StringComparer.Ordinal)
    {
        "ram.rising-trend",
        "cpu.escalating",
        "cpu.periodic-spikes",
        "cpu.thermal-trend",
        "disk.long-running-pressure",
    };

    /// <summary>
    /// True when this insight was produced by a trend-aware rule that analyses
    /// historical telemetry windows rather than the current snapshot.
    /// </summary>
    public bool IsTrend => s_trendIds.Contains(_insight.Id);

    /// <summary>
    /// Controls visibility of the small "Trend" chip shown in the card header
    /// alongside the severity badge — only for trend-aware findings.
    /// </summary>
    public Visibility TrendTagVisibility =>
        IsTrend ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Segoe MDL2 glyph matched to the rule category.
    ///
    /// Grouping logic:
    ///   CPU rules (point-in-time, trend, escalation, spikes): processor icon
    ///   Temperature rules (static threshold, worsening trend):  thermometer icon
    ///   RAM rules (pressure, rising trend, forecast):           memory icon
    ///   GPU rules (utilisation, VRAM pressure):                 display-adapter icon
    ///   Disk rules (space, throughput, long-run, forecast):     storage/transfer icon
    ///   Startup rules (item count, impact, correlation):        power/start icon
    ///   Forecast rules: same glyph as the resource being forecast
    ///   Baseline anomaly rules: same glyph as the primary resource
    ///   Synthesis rules: glyph of the primary contributing resource
    ///   system.healthy: checkmark
    /// </summary>
    public string IconGlyph => _insight.Id switch
    {
        // ── CPU (point-in-time + trend + escalation + spikes + baseline) ──────
        "cpu.sustained-high"      => "",  // Processor
        "cpu.escalating"          => "",  // Processor
        "cpu.periodic-spikes"     => "",  // Processor
        "baseline.cpu-above-norm" => "",  // Processor

        // ── Temperature (static + trend + forecast + synthesis) ───────────────
        "cpu.high-temperature"               => "",  // Thermometer
        "cpu.thermal-trend"                  => "",  // Thermometer
        "forecast.thermal-runaway"           => "",  // Thermometer
        "baseline.thermal-above-norm"        => "",  // Thermometer
        "synthesis.thermal-throttle-cascade" => "",  // Thermometer

        // ── RAM (pressure + trend + forecast + baseline) ──────────────────────
        "ram.high-pressure"       => "",  // Memory
        "ram.rising-trend"        => "",  // Memory
        "forecast.ram-pressure"   => "",  // Memory
        "baseline.ram-above-norm" => "",  // Memory

        // ── GPU / VRAM ────────────────────────────────────────────────────────
        "gpu.unexpected-load" => "",  // Display adapter
        "gpu.vram-pressure"   => "",  // Display adapter

        // ── Disk (space + throughput + long-run + forecast) ───────────────────
        "disk.low-space"             => "",  // Storage
        "disk.sustained-io"          => "",    // Data transfer
        "disk.long-running-pressure" => "",    // Data transfer
        "forecast.disk-exhaustion"   => "",  // Storage

        // ── Startup ───────────────────────────────────────────────────────────
        "startup.many-items"            => "",  // Power/start (reuse processor glyph)
        "startup.high-impact-apps"      => "",  // Power/start
        "startup.resource-pressure"     => "",  // Power/start
        "forecast.startup-degradation"  => "",  // Power/start

        // ── All-clear ─────────────────────────────────────────────────────────
        "system.healthy" => "",  // Checkmark

        // ── Fallback (synthesis.* cross-process correlations + unknowns) ──────
        _ => "",  // Info
    };

    /// <summary>Short severity label for the badge pill.</summary>
    public string SeverityLabel => _insight.Severity switch
    {
        InsightSeverity.Warning        => "Warning",
        InsightSeverity.Recommendation => "Tip",
        _                              => "Info",
    };

    // ── Expandable detail ─────────────────────────────────────────────────────

    public Visibility ExpandedVisibility =>
        IsExpanded ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Chevron glyph that signals the expand / collapse direction.</summary>
    public string ExpandChevron =>
        IsExpanded ? "" : ""; // ChevronUp / ChevronDown

    public bool       HasActionHint       => _insight.ActionHint is not null;
    public Visibility ActionHintVisibility =>
        HasActionHint ? Visibility.Visible : Visibility.Collapsed;

    // ── Recommended actions ───────────────────────────────────────────────────

    /// <summary>
    /// Display-layer wrappers for the rule's recommended actions.
    /// Empty list when the rule provides no actions (e.g. system.healthy).
    /// Refreshed by <see cref="Update"/> when the engine republishes the finding.
    /// </summary>
    public IReadOnlyList<ActionCardViewModel> Actions           => _actions;

    public bool       HasActions       => _actions.Count > 0;
    public Visibility ActionsVisibility =>
        HasActions ? Visibility.Visible : Visibility.Collapsed;

    // ── Process attribution ───────────────────────────────────────────────────

    /// <summary>
    /// Processes identified as contributors to this finding (CPU%, RAM GB, or
    /// GPU heuristic). Empty when no process crosses the attribution threshold
    /// or when the process sampler has not yet produced its first delta.
    ///
    /// GPU attributions have lower confidence (~55 %) and show "Detected"
    /// instead of a numeric usage value in the UI.
    /// </summary>
    public IReadOnlyList<Services.Intelligence.ProcessAttribution> AttributedProcesses =>
        _insight.AttributedProcesses;

    public bool       HasAttributedProcesses  => _insight.AttributedProcesses.Count > 0;
    public Visibility AttributionsVisibility  =>
        HasAttributedProcesses ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Expands or collapses the full detail panel.</summary>
    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    /// <summary>
    /// Notifies the UI that <see cref="RelativeTime"/> has changed.
    /// Called by the owning ViewModel's periodic timestamp timer so that
    /// cards age correctly ("just now" → "45s ago" → "3 min ago") without
    /// waiting for the intelligence engine to republish the finding.
    /// </summary>
    public void RefreshTimestamp() => OnPropertyChanged(nameof(RelativeTime));

    // ── Sparkline ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Pre-built sparkline data for the compact metric history visualizer.
    /// Null for insights without a direct metric mapping (forecast, synthesis,
    /// session, system.healthy-like cases without telemetry).
    /// Refreshed on every <see cref="Update"/> call.
    /// </summary>
    public SparklineData? SparklineData { get; private set; }

    /// <summary>Controls whether the sparkline strip is shown on the card.</summary>
    public Visibility SparklineVisibility =>
        SparklineData is not null ? Visibility.Visible : Visibility.Collapsed;

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>
    /// Callback set by <see cref="InsightsPageViewModel.SetNavigationCallback"/>
    /// so the card can trigger navigation without a hard dependency on Frame.
    /// Null until the page wires it up.
    /// </summary>
    public Action<string>? RequestNavigateToDetail { get; set; }

    /// <summary>Opens the full investigation workspace for this finding.</summary>
    [RelayCommand]
    private void ViewDetails() => RequestNavigateToDetail?.Invoke(_insight.Id);

    // ── Severity → visual mapping ─────────────────────────────────────────────

    // "system.healthy" is an Info finding but deserves green, not blue.
    private static bool IsGoodInfo(string id) => id == "system.healthy";

    private static SolidColorBrush Accent(InsightSeverity s, string id) => s switch
    {
        InsightSeverity.Warning        => s_warningAccent,
        InsightSeverity.Recommendation => s_infoAccent,
        _ => IsGoodInfo(id)            ? s_goodAccent : s_infoAccent,
    };

    private static SolidColorBrush Bg(InsightSeverity s, string id) => s switch
    {
        InsightSeverity.Warning        => s_warningBg,
        InsightSeverity.Recommendation => s_infoBg,
        _ => IsGoodInfo(id)            ? s_goodBg : s_infoBg,
    };

    private static SolidColorBrush Subtle(InsightSeverity s, string id) => s switch
    {
        InsightSeverity.Warning        => s_warningSubtle,
        InsightSeverity.Recommendation => s_infoSubtle,
        _ => IsGoodInfo(id)            ? s_goodSubtle : s_infoSubtle,
    };
}
