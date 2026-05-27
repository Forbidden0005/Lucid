using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Timeline;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lucid.ViewModels;

/// <summary>
/// ViewModel for the Operational Timeline page.
///
/// Architecture
/// ────────────
/// <see cref="FlatItems"/> is an <see cref="ObservableCollection{T}"/> of mixed-type
/// items: <see cref="TimelineGroupHeaderViewModel"/> (section dividers) and
/// <see cref="TimelineEventViewModel"/> (event cards). The page's ListView uses a
/// <c>DataTemplateSelector</c> to render each type with the correct template.
///
/// Time sections (newest-first within each):
///   "Now"             — events in the last 2 minutes
///   "Last 15 Minutes" — 2–15 minutes old
///   "Today"           — 15 minutes to midnight of today
///   "Earlier"         — before today
///
/// Filtering
/// ─────────
/// Five non-exclusive toggle chips: Warnings, Recommendations, Forecasts, Actions, Session.
/// When all are OFF the full list is shown. When any are ON, only matching events are shown.
/// An event can satisfy multiple active filters (OR logic).
///
/// Live updates
/// ────────────
/// Subscribes to <see cref="ITimelineAggregationService.NewEventAdded"/>.
/// On each new event, prepends to <see cref="_allEvents"/> then calls
/// <see cref="RebuildFlatItems"/> to regenerate the grouped, filtered list.
///
/// Cleanup
/// ───────
/// Call <see cref="Cleanup"/> from <c>Page.Unloaded</c> to unsubscribe.
/// </summary>
public sealed partial class TimelinePageViewModel : ObservableObject
{
    private const int MaxDisplayEvents = 500;

    // ── Backing store ─────────────────────────────────────────────────────────

    /// <summary>All known events in newest-first order. Never exceeds MaxDisplayEvents.</summary>
    private readonly List<TimelineEventViewModel> _allEvents = new(MaxDisplayEvents);

    /// <summary>
    /// 30-second timer that calls <see cref="TimelineEventViewModel.RefreshTimestamp"/>
    /// on every visible event so relative-time labels age naturally in the UI
    /// ("just now" → "45s ago" → "3 min ago") without requiring a new event.
    /// </summary>
    private readonly DispatcherTimer _timestampTimer;

    // ── Filter state ──────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isWarningFilterActive;
    [ObservableProperty] private bool _isRecommendationFilterActive;
    [ObservableProperty] private bool _isForecastFilterActive;
    [ObservableProperty] private bool _isActionFilterActive;
    [ObservableProperty] private bool _isSessionFilterActive;

    // ── Output ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Flat mixed list of <see cref="TimelineGroupHeaderViewModel"/> and
    /// <see cref="TimelineEventViewModel"/> items in display order.
    /// Bound to the page's ListView via a DataTemplateSelector.
    /// </summary>
    public ObservableCollection<object> FlatItems { get; } = [];

    // ── Status ────────────────────────────────────────────────────────────────

    [ObservableProperty] private int _totalEventCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
    [NotifyPropertyChangedFor(nameof(ListVisibility))]
    private bool _isEmpty = true;

    public Visibility EmptyStateVisibility =>
        IsEmpty ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ListVisibility =>
        IsEmpty ? Visibility.Collapsed : Visibility.Visible;

    // ── Filter chip brushes (static — one allocation per active/inactive state) ──

    private static readonly SolidColorBrush s_chipActiveBg     = Mk(0x33,  78, 161, 255);
    private static readonly SolidColorBrush s_chipActiveBorder  = Mk(0x99,  78, 161, 255);
    private static readonly SolidColorBrush s_chipActiveText    = Mk(0xFF,  78, 161, 255);
    private static readonly SolidColorBrush s_chipInactiveBg    = Mk(0x14, 255, 255, 255);
    private static readonly SolidColorBrush s_chipInactiveBorder = Mk(0x33, 255, 255, 255);
    private static readonly SolidColorBrush s_chipInactiveText  = Mk(0xCC, 255, 255, 255);

    private static SolidColorBrush Mk(byte a, byte r, byte g, byte b) =>
        new(Color.FromArgb(a, r, g, b));

    private static SolidColorBrush ChipBg(bool on)     => on ? s_chipActiveBg     : s_chipInactiveBg;
    private static SolidColorBrush ChipBorder(bool on) => on ? s_chipActiveBorder : s_chipInactiveBorder;
    private static SolidColorBrush ChipText(bool on)   => on ? s_chipActiveText   : s_chipInactiveText;

    // Warnings chip
    public SolidColorBrush WarningChipBg     => ChipBg(IsWarningFilterActive);
    public SolidColorBrush WarningChipBorder => ChipBorder(IsWarningFilterActive);
    public SolidColorBrush WarningChipText   => ChipText(IsWarningFilterActive);

    // Recommendations chip
    public SolidColorBrush RecommendationChipBg     => ChipBg(IsRecommendationFilterActive);
    public SolidColorBrush RecommendationChipBorder => ChipBorder(IsRecommendationFilterActive);
    public SolidColorBrush RecommendationChipText   => ChipText(IsRecommendationFilterActive);

    // Forecasts chip
    public SolidColorBrush ForecastChipBg     => ChipBg(IsForecastFilterActive);
    public SolidColorBrush ForecastChipBorder => ChipBorder(IsForecastFilterActive);
    public SolidColorBrush ForecastChipText   => ChipText(IsForecastFilterActive);

    // Actions chip
    public SolidColorBrush ActionChipBg     => ChipBg(IsActionFilterActive);
    public SolidColorBrush ActionChipBorder => ChipBorder(IsActionFilterActive);
    public SolidColorBrush ActionChipText   => ChipText(IsActionFilterActive);

    // Session chip
    public SolidColorBrush SessionChipBg     => ChipBg(IsSessionFilterActive);
    public SolidColorBrush SessionChipBorder => ChipBorder(IsSessionFilterActive);
    public SolidColorBrush SessionChipText   => ChipText(IsSessionFilterActive);

    // ── Construction ──────────────────────────────────────────────────────────

    public TimelinePageViewModel()
    {
        // Seed from events already in the aggregation service buffer.
        foreach (var ev in AppServices.Timeline.Events)
            _allEvents.Add(new TimelineEventViewModel(ev));

        RebuildFlatItems();

        // Subscribe to live additions. All fire on UI thread — no dispatching needed.
        AppServices.Timeline.NewEventAdded += OnNewEventAdded;

        // Tick every 30 s to age relative-time labels on existing cards.
        // Mirrors the same pattern used in InsightsPageViewModel / DashboardViewModel.
        _timestampTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timestampTimer.Tick += (_, _) =>
        {
            foreach (var ev in _allEvents)
                ev.RefreshTimestamp();
        };
        _timestampTimer.Start();
    }

    // ── Filter commands ───────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleWarningFilter()
    {
        IsWarningFilterActive = !IsWarningFilterActive;
        NotifyChipBrushes();
        RebuildFlatItems();
    }

    [RelayCommand]
    private void ToggleRecommendationFilter()
    {
        IsRecommendationFilterActive = !IsRecommendationFilterActive;
        NotifyChipBrushes();
        RebuildFlatItems();
    }

    [RelayCommand]
    private void ToggleForecastFilter()
    {
        IsForecastFilterActive = !IsForecastFilterActive;
        NotifyChipBrushes();
        RebuildFlatItems();
    }

    [RelayCommand]
    private void ToggleActionFilter()
    {
        IsActionFilterActive = !IsActionFilterActive;
        NotifyChipBrushes();
        RebuildFlatItems();
    }

    [RelayCommand]
    private void ToggleSessionFilter()
    {
        IsSessionFilterActive = !IsSessionFilterActive;
        NotifyChipBrushes();
        RebuildFlatItems();
    }

    private void NotifyChipBrushes()
    {
        OnPropertyChanged(nameof(WarningChipBg));
        OnPropertyChanged(nameof(WarningChipBorder));
        OnPropertyChanged(nameof(WarningChipText));
        OnPropertyChanged(nameof(RecommendationChipBg));
        OnPropertyChanged(nameof(RecommendationChipBorder));
        OnPropertyChanged(nameof(RecommendationChipText));
        OnPropertyChanged(nameof(ForecastChipBg));
        OnPropertyChanged(nameof(ForecastChipBorder));
        OnPropertyChanged(nameof(ForecastChipText));
        OnPropertyChanged(nameof(ActionChipBg));
        OnPropertyChanged(nameof(ActionChipBorder));
        OnPropertyChanged(nameof(ActionChipText));
        OnPropertyChanged(nameof(SessionChipBg));
        OnPropertyChanged(nameof(SessionChipBorder));
        OnPropertyChanged(nameof(SessionChipText));
    }

    // ── Live update ───────────────────────────────────────────────────────────

    private void OnNewEventAdded(object? sender, TimelineEvent ev)
    {
        // Always on UI thread — safe to mutate ObservableCollections directly.
        _allEvents.Insert(0, new TimelineEventViewModel(ev));

        while (_allEvents.Count > MaxDisplayEvents)
            _allEvents.RemoveAt(_allEvents.Count - 1);

        RebuildFlatItems();
    }

    // ── Flat list builder ─────────────────────────────────────────────────────

    private void RebuildFlatItems()
    {
        var now        = DateTimeOffset.Now;
        var todayStart = new DateTimeOffset(now.Date, now.Offset);

        // Apply filter — _allEvents is already newest-first.
        var filtered = _allEvents.Where(PassesFilter).ToList();

        // Partition into time sections (mutually exclusive, newest-first within each).
        var sectionNow = filtered
            .Where(e => (now - e.OccurredAt).TotalMinutes < 2)
            .ToList();

        var sectionRecent = filtered
            .Where(e => { var m = (now - e.OccurredAt).TotalMinutes; return m >= 2 && m < 15; })
            .ToList();

        var sectionToday = filtered
            .Where(e => (now - e.OccurredAt).TotalMinutes >= 15 && e.OccurredAt >= todayStart)
            .ToList();

        var sectionEarlier = filtered
            .Where(e => e.OccurredAt < todayStart)
            .ToList();

        // Rebuild flat list.
        FlatItems.Clear();
        AppendSection("Now",             sectionNow);
        AppendSection("Last 15 Minutes", sectionRecent);
        AppendSection("Today",           sectionToday);
        AppendSection("Earlier",         sectionEarlier);

        TotalEventCount = filtered.Count;
        IsEmpty         = filtered.Count == 0;
        // EmptyStateVisibility and ListVisibility are notified automatically
        // by [NotifyPropertyChangedFor] on _isEmpty.
    }

    private void AppendSection(string label, List<TimelineEventViewModel> events)
    {
        if (events.Count == 0) return;
        FlatItems.Add(new TimelineGroupHeaderViewModel(label, events.Count));
        foreach (var ev in events) FlatItems.Add(ev);
    }

    // ── Filter predicate ──────────────────────────────────────────────────────

    private bool PassesFilter(TimelineEventViewModel ev)
    {
        // No active filters → show everything.
        bool anyActive = IsWarningFilterActive
                      || IsRecommendationFilterActive
                      || IsForecastFilterActive
                      || IsActionFilterActive
                      || IsSessionFilterActive;
        if (!anyActive) return true;

        // OR logic — passes if it matches any active filter.
        if (IsWarningFilterActive &&
            ev.Severity is TimelineEventSeverity.Warning or TimelineEventSeverity.Critical)
            return true;

        if (IsRecommendationFilterActive &&
            ev.Type    == TimelineEventType.InsightOnset &&
            ev.Severity == TimelineEventSeverity.Info)
            return true;

        if (IsForecastFilterActive && ev.Type == TimelineEventType.ForecastAlert)
            return true;

        if (IsActionFilterActive && ev.Type is
            TimelineEventType.ActionExecuted or TimelineEventType.ActionPreview
            or TimelineEventType.ActionRollback or TimelineEventType.ActionFailed)
            return true;

        if (IsSessionFilterActive && ev.Type is
            TimelineEventType.SystemBoot or TimelineEventType.SessionStart
            or TimelineEventType.WakeFromSleep or TimelineEventType.IdleBegin
            or TimelineEventType.IdleEnd or TimelineEventType.NarrativeCheckpoint)
            return true;

        return false;
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Unsubscribes from the timeline service.
    /// Call from <c>Page.Unloaded</c> to prevent event-handler leaks on navigation.
    /// </summary>
    public void Cleanup()
    {
        _timestampTimer.Stop();
        AppServices.Timeline.NewEventAdded -= OnNewEventAdded;
    }
}
