using CommunityToolkit.Mvvm.ComponentModel;
using ExplainMyPC.Services.Narrative;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ExplainMyPC.ViewModels;

/// <summary>
/// Display-layer wrapper for <see cref="OperationalNarrative"/> produced by the
/// <see cref="IOperationalNarrativeEngine"/>.
///
/// Translates the data record into ready-to-bind view properties:
///   — Paragraphs     : IReadOnlyList&lt;string&gt; for an ItemsRepeater
///   — HealthState*   : brushes, glyphs, and labels for the state indicator
///   — Headline       : short verdict string
///   — Generated*     : timestamp text
///   — Count text     : "3 active findings"
///
/// Static brush palette:
///   One SolidColorBrush per health state is allocated at class init time
///   and shared across all instances.
///
/// Threading:
///   Subscribe / unsubscribe must happen on the UI thread.
///   NarrativeUpdated fires on the UI thread, so OnPropertyChanged is always
///   safe to call directly.
///
/// Usage:
///   Instantiate in a page constructor; call Cleanup() from page Unloaded.
/// </summary>
public sealed partial class NarrativeViewModel : ObservableObject
{
    // ── Static brush palette ──────────────────────────────────────────────────

    // Accent colours per health state
    private static readonly SolidColorBrush s_healthyBrush   = Mk(255,  87, 214, 141); // green
    private static readonly SolidColorBrush s_monitoringBrush = Mk(255,  78, 161, 255); // blue
    private static readonly SolidColorBrush s_degradedBrush  = Mk(255, 255, 179,  71); // amber
    private static readonly SolidColorBrush s_criticalBrush  = Mk(255, 255,  80,  80); // red

    // Subtle background tints (12 % alpha) for the state pill
    private static readonly SolidColorBrush s_healthyBg    = Mk(0x20,  87, 214, 141);
    private static readonly SolidColorBrush s_monitoringBg = Mk(0x20,  78, 161, 255);
    private static readonly SolidColorBrush s_degradedBg   = Mk(0x20, 255, 179,  71);
    private static readonly SolidColorBrush s_criticalBg   = Mk(0x20, 255,  80,  80);

    private static SolidColorBrush Mk(byte a, byte r, byte g, byte b) =>
        new(Color.FromArgb(a, r, g, b));

    // ── Backing state ─────────────────────────────────────────────────────────

    private OperationalNarrative? _narrative;

    // ── Construction ──────────────────────────────────────────────────────────

    public NarrativeViewModel()
    {
        AppServices.Narrative.NarrativeUpdated += OnNarrativeUpdated;

        // Seed immediately if a narrative has already been generated.
        if (AppServices.Narrative.CurrentNarrative is { } existing)
            Apply(existing);
    }

    // ── Narrative event ───────────────────────────────────────────────────────

    private void OnNarrativeUpdated(object? sender, OperationalNarrative narrative)
        => Apply(narrative);

    private void Apply(OperationalNarrative narrative)
    {
        _narrative = narrative;

        // Fire all property updates at once. OnPropertyChanged("") refreshes
        // all OneWay bindings simultaneously with a single dispatch.
        OnPropertyChanged(string.Empty);
    }

    // ── Content ───────────────────────────────────────────────────────────────

    /// <summary>Short verdict sentence, e.g. "Your system is running well."</summary>
    public string Headline => _narrative?.Headline ?? "Analyzing your system…";

    /// <summary>
    /// The full ordered set of narrative paragraphs.
    /// Bind an ItemsRepeater to this for paragraph-by-paragraph display.
    /// </summary>
    public IReadOnlyList<string> Paragraphs =>
        _narrative?.Paragraphs ?? [];

    public bool HasParagraphs => (_narrative?.Paragraphs.Length ?? 0) > 0;

    /// <summary>True once the first narrative has been generated.</summary>
    public bool IsReady => _narrative is not null;

    public Visibility ReadyVisibility    => Vis(IsReady);
    public Visibility LoadingVisibility  => Vis(!IsReady);

    // ── Health state ──────────────────────────────────────────────────────────

    public SystemHealthState HealthState =>
        _narrative?.HealthState ?? SystemHealthState.Healthy;

    public bool IsHealthy    => HealthState == SystemHealthState.Healthy;
    public bool IsMonitoring => HealthState == SystemHealthState.Monitoring;
    public bool IsDegraded   => HealthState == SystemHealthState.Degraded;
    public bool IsCritical   => HealthState == SystemHealthState.Critical;

    /// <summary>Foreground accent colour for the health state indicator.</summary>
    public SolidColorBrush HealthStateBrush => HealthState switch
    {
        SystemHealthState.Healthy    => s_healthyBrush,
        SystemHealthState.Monitoring => s_monitoringBrush,
        SystemHealthState.Degraded   => s_degradedBrush,
        _                            => s_criticalBrush,
    };

    /// <summary>Subtle background tint for the health state pill / badge.</summary>
    public SolidColorBrush HealthStateBg => HealthState switch
    {
        SystemHealthState.Healthy    => s_healthyBg,
        SystemHealthState.Monitoring => s_monitoringBg,
        SystemHealthState.Degraded   => s_degradedBg,
        _                            => s_criticalBg,
    };

    /// <summary>Segoe MDL2 / Fluent icon glyph for the current health state.</summary>
    public string HealthStateGlyph => HealthState switch
    {
        SystemHealthState.Healthy    => "",  // CheckMark
        SystemHealthState.Monitoring => "",  // Info
        SystemHealthState.Degraded   => "",  // Warning
        _                            => "",  // ErrorBadge / Critical
    };

    /// <summary>Short label for the health state pill.</summary>
    public string HealthStateLabel => HealthState switch
    {
        SystemHealthState.Healthy    => "Healthy",
        SystemHealthState.Monitoring => "Monitoring",
        SystemHealthState.Degraded   => "Degraded",
        _                            => "Critical",
    };

    // ── Counts ────────────────────────────────────────────────────────────────

    public int ActiveWarnings => _narrative?.ActiveWarnings ?? 0;
    public int ActiveFindings => _narrative?.ActiveFindings ?? 0;

    public string ActiveFindingsText => ActiveFindings switch
    {
        0 => "No active findings",
        1 => "1 active finding",
        _ => $"{ActiveFindings} active findings",
    };

    public string ActiveWarningsText => ActiveWarnings switch
    {
        0 => string.Empty,
        1 => "1 warning",
        _ => $"{ActiveWarnings} warnings",
    };

    public Visibility WarningCountVisibility => Vis(ActiveWarnings > 0);

    // ── Timestamp ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Human-readable age of the narrative: "just now", "45s ago", "3 min ago".
    /// Refreshed by Apply() when a new narrative arrives.
    /// </summary>
    public string GeneratedTimeText
    {
        get
        {
            if (_narrative is null) return string.Empty;
            var age = DateTimeOffset.Now - _narrative.GeneratedAt;
            if (age.TotalSeconds < 20) return "just now";
            if (age.TotalMinutes < 1)  return $"{(int)age.TotalSeconds}s ago";
            if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes} min ago";
            return $"{(int)age.TotalHours}h ago";
        }
    }

    // ── Multi-paragraph count ─────────────────────────────────────────────────

    /// <summary>Number of body paragraphs (excludes headline).</summary>
    public int ParagraphCount => _narrative?.Paragraphs.Length ?? 0;

    public string ParagraphCountText => ParagraphCount switch
    {
        1 => "1 section",
        _ => $"{ParagraphCount} sections",
    };

    // ── Cleanup ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Unsubscribes from the narrative engine.
    /// Call from the host page's Unloaded handler.
    /// </summary>
    public void Cleanup() =>
        AppServices.Narrative.NarrativeUpdated -= OnNarrativeUpdated;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Visibility Vis(bool show) =>
        show ? Visibility.Visible : Visibility.Collapsed;
}
