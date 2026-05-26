using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Timeline;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lucid.ViewModels;

/// <summary>
/// Display-layer wrapper for a <see cref="TimelineEvent"/>.
///
/// Exposes all presentation-layer properties needed to render a timeline event card:
/// Segoe MDL2 glyph, severity-tinted brushes, formatted relative time, type label,
/// process attribution text, and expand/collapse state.
///
/// Static brush palette:
///   One SolidColorBrush per colour × role, allocated at class init time and shared
///   across every card instance. Zero allocations per event render.
///
/// IsExpanded:
///   Observable — toggled by <see cref="ToggleExpandCommand"/>. Drives
///   <see cref="ExpandedContentVisibility"/> and <see cref="ExpandGlyph"/>.
///   Changes are propagated to dependent display properties via
///   <see cref="OnIsExpandedChanged"/>.
/// </summary>
public sealed partial class TimelineEventViewModel : ObservableObject
{
    // ── Static brush palette — one allocation per tier × role, ever ───────────

    private static readonly SolidColorBrush s_goodFg     = Mk(255,  87, 214, 141); // #57D68D
    private static readonly SolidColorBrush s_infoFg     = Mk(255,  78, 161, 255); // #4EA1FF
    private static readonly SolidColorBrush s_warningFg  = Mk(255, 255, 179,  71); // #FFB347
    private static readonly SolidColorBrush s_criticalFg = Mk(255, 255, 107, 107); // #FF6B6B

    private static readonly SolidColorBrush s_goodBg     = Mk(0x26,  87, 214, 141);
    private static readonly SolidColorBrush s_infoBg     = Mk(0x26,  78, 161, 255);
    private static readonly SolidColorBrush s_warningBg  = Mk(0x26, 255, 179,  71);
    private static readonly SolidColorBrush s_criticalBg = Mk(0x26, 255, 107, 107);

    private static SolidColorBrush Mk(byte a, byte r, byte g, byte b) =>
        new(Color.FromArgb(a, r, g, b));

    // ── Backing event ─────────────────────────────────────────────────────────

    /// <summary>The underlying immutable event record.</summary>
    public TimelineEvent Source { get; }

    // ── Expand state ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isExpanded;

    // Propagate to computed display properties on expand/collapse.
    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ExpandGlyph));
        OnPropertyChanged(nameof(ExpandedContentVisibility));
    }

    // ── Construction ──────────────────────────────────────────────────────────

    public TimelineEventViewModel(TimelineEvent source) => Source = source;

    // ── Passthrough properties ────────────────────────────────────────────────

    public DateTimeOffset        OccurredAt => Source.OccurredAt;
    public TimelineEventType     Type       => Source.Type;
    public TimelineEventSeverity Severity   => Source.Severity;
    public string                Title      => Source.Title;
    public string?               Detail     => Source.Detail;

    // ── Glyph mapping ─────────────────────────────────────────────────────────

    /// <summary>Segoe MDL2 Assets glyph for this event type.</summary>
    public string Glyph => Source.Type switch
    {
        TimelineEventType.SystemBoot          => "", // Power
        TimelineEventType.SessionStart        => "", // Play filled
        TimelineEventType.WakeFromSleep       => "", // Brightness / Sun
        TimelineEventType.IdleBegin           => "", // Clock
        TimelineEventType.IdleEnd             => "", // Refresh / Resume
        TimelineEventType.InsightResolved     => "", // Checkmark
        TimelineEventType.ForecastAlert       => "", // Trending up
        TimelineEventType.SynthesisCorrelated => "", // Brain / Neural
        TimelineEventType.NarrativeCheckpoint => "", // Document / Page
        TimelineEventType.ActionExecuted      => "", // Play filled
        TimelineEventType.ActionPreview       => "", // Preview / Binoculars
        TimelineEventType.ActionRollback      => "", // Undo
        TimelineEventType.ActionFailed        => "", // Error badge

        // InsightOnset: warning glyph for Warning/Critical, info otherwise
        TimelineEventType.InsightOnset when
            Source.Severity is TimelineEventSeverity.Warning
                           or TimelineEventSeverity.Critical => "", // Warning triangle
        TimelineEventType.InsightOnset => "",                       // Info circle

        _ => "",
    };

    // ── Accent colours ────────────────────────────────────────────────────────

    public SolidColorBrush AccentBrush => Source.Severity switch
    {
        TimelineEventSeverity.Good     => s_goodFg,
        TimelineEventSeverity.Warning  => s_warningFg,
        TimelineEventSeverity.Critical => s_criticalFg,
        _                              => s_infoFg,
    };

    public SolidColorBrush AccentBackground => Source.Severity switch
    {
        TimelineEventSeverity.Good     => s_goodBg,
        TimelineEventSeverity.Warning  => s_warningBg,
        TimelineEventSeverity.Critical => s_criticalBg,
        _                              => s_infoBg,
    };

    // ── Type label chip ───────────────────────────────────────────────────────

    /// <summary>Short category label shown in the pill badge on the card.</summary>
    public string TypeLabel => Source.Type switch
    {
        TimelineEventType.SystemBoot or
        TimelineEventType.SessionStart or
        TimelineEventType.WakeFromSleep or
        TimelineEventType.IdleBegin or
        TimelineEventType.IdleEnd             => "Session",
        TimelineEventType.InsightOnset when
            Source.Severity == TimelineEventSeverity.Warning => "Warning",
        TimelineEventType.InsightOnset        => "Info",
        TimelineEventType.InsightResolved     => "Resolved",
        TimelineEventType.ForecastAlert       => "Forecast",
        TimelineEventType.SynthesisCorrelated => "Correlated",
        TimelineEventType.NarrativeCheckpoint => "Summary",
        TimelineEventType.ActionExecuted      => "Action",
        TimelineEventType.ActionPreview       => "Preview",
        TimelineEventType.ActionRollback      => "Rollback",
        TimelineEventType.ActionFailed        => "Failed",
        _                                     => "Event",
    };

    // ── Time display ──────────────────────────────────────────────────────────

    /// <summary>Human-readable relative age — e.g. "just now", "4 min ago", "2h ago".</summary>
    public string RelativeTime
    {
        get
        {
            var age = DateTimeOffset.Now - Source.OccurredAt;
            if (age.TotalSeconds <  5)  return "just now";
            if (age.TotalSeconds < 60)  return $"{(int)age.TotalSeconds}s ago";
            if (age.TotalMinutes < 60)  return $"{(int)age.TotalMinutes} min ago";
            if (age.TotalHours   < 24)  return $"{(int)age.TotalHours}h ago";
            return Source.OccurredAt.LocalDateTime.ToString("MMM d, h:mm tt");
        }
    }

    /// <summary>
    /// Notifies the UI that RelativeTime has changed.
    /// Called by <see cref="TimelinePageViewModel"/>'s periodic 30-second timer
    /// so cards age correctly ("just now" → "45s ago" → "3 min ago") without
    /// requiring a new event from the intelligence engine.
    /// </summary>
    public void RefreshTimestamp() => OnPropertyChanged(nameof(RelativeTime));

    /// <summary>Precise local clock time — shown in the expanded detail.</summary>
    public string TimestampText =>
        Source.OccurredAt.LocalDateTime.ToString("h:mm:ss tt");

    // ── Expand toggle ─────────────────────────────────────────────────────────

    /// <summary>Hides the expand toggle for events with no expandable content.</summary>
    public Visibility ExpandToggleVisibility =>
        HasDetail || HasProcesses ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Shows/hides the expanded content area.</summary>
    public Visibility ExpandedContentVisibility =>
        IsExpanded ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Chevron direction glyph — flips when expanded.</summary>
    public string ExpandGlyph => IsExpanded ? "" : ""; // ChevronUp / ChevronDown

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    // ── Process attribution ───────────────────────────────────────────────────

    public bool HasProcesses =>
        Source.AttributedProcesses is { Count: > 0 };

    /// <summary>Comma-joined attributed process names for the attribution line.</summary>
    public string ProcessesText =>
        Source.AttributedProcesses is { Count: > 0 }
            ? string.Join(", ", Source.AttributedProcesses)
            : string.Empty;

    public Visibility ProcessesVisibility =>
        HasProcesses ? Visibility.Visible : Visibility.Collapsed;

    // ── Detail ────────────────────────────────────────────────────────────────

    public bool HasDetail => !string.IsNullOrWhiteSpace(Source.Detail);
}
