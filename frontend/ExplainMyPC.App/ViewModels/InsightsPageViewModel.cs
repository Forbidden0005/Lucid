using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplainMyPC.Services.Intelligence;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ExplainMyPC.ViewModels;

// ── Tab / filter enums ────────────────────────────────────────────────────────

/// <summary>
/// The four content tabs of the Intelligence Workspace.
/// </summary>
public enum InsightTab
{
    /// <summary>All active findings sorted by the current sort mode.</summary>
    All        = 0,

    /// <summary>Only trend-aware rule findings (ram.rising-trend, cpu.escalating, etc.).</summary>
    Trends     = 1,

    /// <summary>Only cross-insight synthesis findings (Id starts with "synthesis.").</summary>
    Correlated = 2,

    /// <summary>Persisted operation history from IOperationHistoryService.</summary>
    History    = 3,
}

/// <summary>
/// Severity tier filter applied within the All / Trends / Correlated tabs.
/// </summary>
public enum InsightFilter
{
    All         = 0,
    Warnings    = 1,
    Tips        = 2,
    Info        = 3,
}

// ── ViewModel ─────────────────────────────────────────────────────────────────

/// <summary>
/// Drives the InsightsPage — the full-page Intelligence Workspace.
///
/// Architecture:
///   Subscribes to AppServices.Intelligence.InsightsUpdated for live findings.
///   Maintains a shared InsightCardViewModel cache (same pattern as
///   DashboardViewModel) that preserves IsExpanded state across engine re-ticks.
///
///   Three finding tabs:
///     All       — all active findings from the engine, filterable by severity
///     Trends    — findings where IsTrend is true (trend-aware rule IDs)
///     Correlated — findings where IsCorrelated is true (synthesis.* IDs)
///
///   History tab:
///     Loads up to 100 records from IOperationHistoryService on first switch.
///     A Refresh command reloads it on demand.
///
///   Filter + Sort:
///     Severity filter (All / Warnings / Tips / Info) applies to all three
///     finding tabs. Sort mode (Confidence ↓ / Severity / Newest) applies
///     to all three finding tabs. Both are preserved across tab switches.
///
///   Visual state:
///     All tab/filter/sort state is exposed as Visibility, SolidColorBrush,
///     and bool properties — no converters required in XAML.
/// </summary>
public sealed partial class InsightsPageViewModel : ObservableObject
{
    // ── Static brush palette ── one allocation per state, ever ────────────────

    // Tab text: bright white when active, muted gray when inactive
    private static readonly SolidColorBrush s_tabActiveBrush   = new(Color.FromArgb(255, 255, 255, 255));
    private static readonly SolidColorBrush s_tabInactiveBrush = new(Color.FromArgb(0x80, 180, 180, 180));

    // Filter / sort chip: info-blue tint when active, subtle white when inactive
    private static readonly SolidColorBrush s_chipActiveBg    = new(Color.FromArgb(0x26, 78, 161, 255));
    private static readonly SolidColorBrush s_chipInactiveBg  = new(Color.FromArgb(0x0A, 255, 255, 255));
    private static readonly SolidColorBrush s_chipActiveFg    = new(Color.FromArgb(255,  78, 161, 255));
    private static readonly SolidColorBrush s_chipInactiveFg  = new(Color.FromArgb(0x80, 200, 200, 200));

    private static SolidColorBrush ChipBg(bool active) => active ? s_chipActiveBg : s_chipInactiveBg;
    private static SolidColorBrush ChipFg(bool active) => active ? s_chipActiveFg : s_chipInactiveFg;

    // ── Observable tab state ──────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllTab))]
    [NotifyPropertyChangedFor(nameof(IsTrendsTab))]
    [NotifyPropertyChangedFor(nameof(IsCorrelatedTab))]
    [NotifyPropertyChangedFor(nameof(IsHistoryTab))]
    [NotifyPropertyChangedFor(nameof(AllTabIndicatorVisibility))]
    [NotifyPropertyChangedFor(nameof(TrendsTabIndicatorVisibility))]
    [NotifyPropertyChangedFor(nameof(CorrelatedTabIndicatorVisibility))]
    [NotifyPropertyChangedFor(nameof(HistoryTabIndicatorVisibility))]
    [NotifyPropertyChangedFor(nameof(AllTabBrush))]
    [NotifyPropertyChangedFor(nameof(TrendsTabBrush))]
    [NotifyPropertyChangedFor(nameof(CorrelatedTabBrush))]
    [NotifyPropertyChangedFor(nameof(HistoryTabBrush))]
    [NotifyPropertyChangedFor(nameof(FindingsPanelVisibility))]
    [NotifyPropertyChangedFor(nameof(HistoryPanelVisibility))]
    [NotifyPropertyChangedFor(nameof(FilterBarVisibility))]
    private InsightTab _activeTab = InsightTab.All;

    // ── Observable filter state ───────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterAll))]
    [NotifyPropertyChangedFor(nameof(IsFilterWarnings))]
    [NotifyPropertyChangedFor(nameof(IsFilterTips))]
    [NotifyPropertyChangedFor(nameof(IsFilterInfo))]
    [NotifyPropertyChangedFor(nameof(FilterAllBackground))]
    [NotifyPropertyChangedFor(nameof(FilterWarningsBackground))]
    [NotifyPropertyChangedFor(nameof(FilterTipsBackground))]
    [NotifyPropertyChangedFor(nameof(FilterInfoBackground))]
    [NotifyPropertyChangedFor(nameof(FilterAllBrush))]
    [NotifyPropertyChangedFor(nameof(FilterWarningsBrush))]
    [NotifyPropertyChangedFor(nameof(FilterTipsBrush))]
    [NotifyPropertyChangedFor(nameof(FilterInfoBrush))]
    private InsightFilter _activeFilter = InsightFilter.All;

    // ── Observable sort state ─────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortConfidenceBackground))]
    [NotifyPropertyChangedFor(nameof(SortSeverityBackground))]
    [NotifyPropertyChangedFor(nameof(SortNewestBackground))]
    [NotifyPropertyChangedFor(nameof(SortConfidenceBrush))]
    [NotifyPropertyChangedFor(nameof(SortSeverityBrush))]
    [NotifyPropertyChangedFor(nameof(SortNewestBrush))]
    private int _activeSortIndex = 0; // 0 = Confidence, 1 = Severity, 2 = Newest

    // ── Tab derived properties ─────────────────────────────────────────────────

    public bool IsAllTab        => ActiveTab == InsightTab.All;
    public bool IsTrendsTab     => ActiveTab == InsightTab.Trends;
    public bool IsCorrelatedTab => ActiveTab == InsightTab.Correlated;
    public bool IsHistoryTab    => ActiveTab == InsightTab.History;

    public Visibility AllTabIndicatorVisibility        => Vis(IsAllTab);
    public Visibility TrendsTabIndicatorVisibility     => Vis(IsTrendsTab);
    public Visibility CorrelatedTabIndicatorVisibility => Vis(IsCorrelatedTab);
    public Visibility HistoryTabIndicatorVisibility    => Vis(IsHistoryTab);

    public SolidColorBrush AllTabBrush        => IsAllTab        ? s_tabActiveBrush : s_tabInactiveBrush;
    public SolidColorBrush TrendsTabBrush     => IsTrendsTab     ? s_tabActiveBrush : s_tabInactiveBrush;
    public SolidColorBrush CorrelatedTabBrush => IsCorrelatedTab ? s_tabActiveBrush : s_tabInactiveBrush;
    public SolidColorBrush HistoryTabBrush    => IsHistoryTab    ? s_tabActiveBrush : s_tabInactiveBrush;

    public Visibility FindingsPanelVisibility => Vis(!IsHistoryTab);
    public Visibility HistoryPanelVisibility  => Vis(IsHistoryTab);
    public Visibility FilterBarVisibility     => Vis(!IsHistoryTab);

    // ── Filter derived properties ──────────────────────────────────────────────

    public bool IsFilterAll      => ActiveFilter == InsightFilter.All;
    public bool IsFilterWarnings => ActiveFilter == InsightFilter.Warnings;
    public bool IsFilterTips     => ActiveFilter == InsightFilter.Tips;
    public bool IsFilterInfo     => ActiveFilter == InsightFilter.Info;

    public SolidColorBrush FilterAllBackground      => ChipBg(IsFilterAll);
    public SolidColorBrush FilterWarningsBackground => ChipBg(IsFilterWarnings);
    public SolidColorBrush FilterTipsBackground     => ChipBg(IsFilterTips);
    public SolidColorBrush FilterInfoBackground     => ChipBg(IsFilterInfo);

    public SolidColorBrush FilterAllBrush           => ChipFg(IsFilterAll);
    public SolidColorBrush FilterWarningsBrush      => ChipFg(IsFilterWarnings);
    public SolidColorBrush FilterTipsBrush          => ChipFg(IsFilterTips);
    public SolidColorBrush FilterInfoBrush          => ChipFg(IsFilterInfo);

    // ── Sort derived properties ────────────────────────────────────────────────

    public SolidColorBrush SortConfidenceBackground => ChipBg(ActiveSortIndex == 0);
    public SolidColorBrush SortSeverityBackground   => ChipBg(ActiveSortIndex == 1);
    public SolidColorBrush SortNewestBackground     => ChipBg(ActiveSortIndex == 2);

    public SolidColorBrush SortConfidenceBrush      => ChipFg(ActiveSortIndex == 0);
    public SolidColorBrush SortSeverityBrush        => ChipFg(ActiveSortIndex == 1);
    public SolidColorBrush SortNewestBrush          => ChipFg(ActiveSortIndex == 2);

    // ── Stats badges ──────────────────────────────────────────────────────────

    private int _allCount;
    private int _warningCount;
    private int _trendCount;
    private int _correlatedCount;

    public string AllCountText        => _allCount.ToString();
    public string WarningCountText    => _warningCount.ToString();
    public string TrendCountText      => _trendCount.ToString();
    public string CorrelatedCountText => _correlatedCount.ToString();

    public Visibility WarningBadgeVisibility    => Vis(_warningCount > 0);
    public Visibility TrendBadgeVisibility      => Vis(_trendCount > 0);
    public Visibility CorrelatedBadgeVisibility => Vis(_correlatedCount > 0);

    // ── Displayed findings ────────────────────────────────────────────────────

    /// <summary>
    /// The current filtered + sorted insight cards for the active tab.
    /// Updated whenever the engine publishes new findings or the tab /
    /// filter / sort mode changes.
    /// </summary>
    public IReadOnlyList<InsightCardViewModel> DisplayedInsights { get; private set; } = [];

    public bool HasDisplayedInsights => DisplayedInsights.Count > 0;

    public string FindingsCountText =>
        $"{DisplayedInsights.Count} finding{(DisplayedInsights.Count == 1 ? "" : "s")}";

    public Visibility FindingsListVisibility => Vis(HasDisplayedInsights);
    public Visibility NoResultsVisibility    => Vis(!HasDisplayedInsights && !IsHistoryTab);

    public string EmptyStateMessage => ActiveTab switch
    {
        InsightTab.Trends     =>
            "No trend findings are active. Trend rules require 2–8 minutes of " +
            "telemetry history before they can fire.",
        InsightTab.Correlated =>
            "No correlated findings detected. Correlations appear when multiple " +
            "independent rules attribute the same process as a contributor.",
        _ => ActiveFilter == InsightFilter.All
            ? "No active findings. Your system is running well."
            : "No findings match the current severity filter.",
    };

    // ── Card cache ────────────────────────────────────────────────────────────

    /// <summary>
    /// Preserves IsExpanded state across engine re-evaluations.
    /// Same pattern as DashboardViewModel._cardCache.
    /// </summary>
    private readonly Dictionary<string, InsightCardViewModel> _cardCache = new();

    // ── History ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Up to 100 most-recent operation records, loaded when switching to the
    /// History tab. Refreshed on demand via <see cref="RefreshHistoryCommand"/>.
    /// </summary>
    public IReadOnlyList<HistoryRecordViewModel> HistoryRecords { get; private set; } = [];

    private bool _isHistoryLoading;
    private bool _historyLoaded;   // true once the first load completes

    public bool HasHistory    => HistoryRecords.Count > 0;
    public bool HasNoHistory  => !HasHistory && !_isHistoryLoading;

    public Visibility HistoryLoadingVisibility => Vis(_isHistoryLoading);
    public Visibility HistoryListVisibility    => Vis(HasHistory);
    public Visibility HistoryEmptyVisibility   => Vis(HasNoHistory);

    public string HistoryCountText =>
        $"{HistoryRecords.Count} record{(HistoryRecords.Count == 1 ? "" : "s")}";

    // ── Construction ──────────────────────────────────────────────────────────

    public InsightsPageViewModel()
    {
        AppServices.Intelligence.InsightsUpdated += OnInsightsUpdated;

        // Seed immediately from whatever the engine already has (back-nav / late init).
        ApplyInsights(AppServices.Intelligence.CurrentInsights);
    }

    // ── CommunityToolkit partial hooks ────────────────────────────────────────

    partial void OnActiveTabChanged(InsightTab value)
    {
        if (value == InsightTab.History)
            _ = LoadHistoryAsync(force: false);
        else
            RebuildDisplay();
    }

    partial void OnActiveFilterChanged(InsightFilter value)
    {
        if (ActiveTab != InsightTab.History)
            RebuildDisplay();
    }

    partial void OnActiveSortIndexChanged(int value)
    {
        if (ActiveTab != InsightTab.History)
            RebuildDisplay();
    }

    // ── Tab commands ──────────────────────────────────────────────────────────

    [RelayCommand] private void SelectTabAll()        => ActiveTab = InsightTab.All;
    [RelayCommand] private void SelectTabTrends()     => ActiveTab = InsightTab.Trends;
    [RelayCommand] private void SelectTabCorrelated() => ActiveTab = InsightTab.Correlated;
    [RelayCommand] private void SelectTabHistory()    => ActiveTab = InsightTab.History;

    // ── Filter commands ───────────────────────────────────────────────────────

    [RelayCommand] private void FilterAll()      => ActiveFilter = InsightFilter.All;
    [RelayCommand] private void FilterWarnings() => ActiveFilter = InsightFilter.Warnings;
    [RelayCommand] private void FilterTips()     => ActiveFilter = InsightFilter.Tips;
    [RelayCommand] private void FilterInfo()     => ActiveFilter = InsightFilter.Info;

    // ── Sort commands ─────────────────────────────────────────────────────────

    [RelayCommand] private void SortByConfidence() => ActiveSortIndex = 0;
    [RelayCommand] private void SortBySeverity()   => ActiveSortIndex = 1;
    [RelayCommand] private void SortByNewest()     => ActiveSortIndex = 2;

    // ── History commands ──────────────────────────────────────────────────────

    /// <summary>Reloads operation history unconditionally.</summary>
    [RelayCommand]
    private Task RefreshHistoryAsync() => LoadHistoryAsync(force: true);

    // ── Intelligence feed ─────────────────────────────────────────────────────

    private void OnInsightsUpdated(object? sender, IReadOnlyList<SystemInsight> insights)
        => ApplyInsights(insights);

    private void ApplyInsights(IReadOnlyList<SystemInsight> insights)
    {
        // ── Sync card cache (same algorithm as DashboardViewModel) ────────────
        var activeIds = new HashSet<string>(insights.Select(i => i.Id));
        foreach (var stale in _cardCache.Keys.Where(k => !activeIds.Contains(k)).ToList())
            _cardCache.Remove(stale);

        foreach (var insight in insights)
        {
            if (_cardCache.TryGetValue(insight.Id, out var existing))
                existing.Update(insight);
            else
                _cardCache[insight.Id] = new InsightCardViewModel(insight);
        }

        // ── Recompute stats ───────────────────────────────────────────────────
        _allCount        = insights.Count;
        _warningCount    = insights.Count(i => i.Severity == InsightSeverity.Warning);
        _trendCount      = _cardCache.Values.Count(c => c.IsTrend);
        _correlatedCount = _cardCache.Values.Count(c => c.IsCorrelated);

        OnPropertyChanged(nameof(AllCountText));
        OnPropertyChanged(nameof(WarningCountText));
        OnPropertyChanged(nameof(TrendCountText));
        OnPropertyChanged(nameof(CorrelatedCountText));
        OnPropertyChanged(nameof(WarningBadgeVisibility));
        OnPropertyChanged(nameof(TrendBadgeVisibility));
        OnPropertyChanged(nameof(CorrelatedBadgeVisibility));

        if (ActiveTab != InsightTab.History)
            RebuildDisplay();
    }

    // ── Display rebuild ───────────────────────────────────────────────────────

    /// <summary>
    /// Applies the active tab, severity filter, and sort order to the card
    /// cache and publishes the result as <see cref="DisplayedInsights"/>.
    /// </summary>
    private void RebuildDisplay()
    {
        // Step 1 — Tab filter
        IEnumerable<InsightCardViewModel> source = ActiveTab switch
        {
            InsightTab.Trends     => _cardCache.Values.Where(c => c.IsTrend),
            InsightTab.Correlated => _cardCache.Values.Where(c => c.IsCorrelated),
            _                     => _cardCache.Values,
        };

        // Step 2 — Severity filter
        source = ActiveFilter switch
        {
            InsightFilter.Warnings => source.Where(c => c.Severity == InsightSeverity.Warning),
            InsightFilter.Tips     => source.Where(c => c.Severity == InsightSeverity.Recommendation),
            InsightFilter.Info     => source.Where(c => c.Severity == InsightSeverity.Info),
            _                      => source,
        };

        // Step 3 — Sort
        source = ActiveSortIndex switch
        {
            1 => source.OrderByDescending(c => (int)c.Severity),
            2 => source.OrderByDescending(c => c.DetectedAt),
            _ => source.OrderByDescending(c => c.ConfidencePercent),
        };

        DisplayedInsights = source.ToList();

        OnPropertyChanged(nameof(DisplayedInsights));
        OnPropertyChanged(nameof(HasDisplayedInsights));
        OnPropertyChanged(nameof(FindingsCountText));
        OnPropertyChanged(nameof(FindingsListVisibility));
        OnPropertyChanged(nameof(NoResultsVisibility));
        OnPropertyChanged(nameof(EmptyStateMessage));
    }

    // ── History load ──────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches up to 100 records from the history service.
    /// Skips the load when <paramref name="force"/> is false and results
    /// have already been loaded this session.
    /// </summary>
    private async Task LoadHistoryAsync(bool force)
    {
        if (!force && _historyLoaded) return;

        _isHistoryLoading = true;
        OnPropertyChanged(nameof(HistoryLoadingVisibility));
        OnPropertyChanged(nameof(HistoryEmptyVisibility));

        try
        {
            var records = await AppServices.HistoryService
                .GetRecentAsync(100)
                .ConfigureAwait(true);  // stay on UI thread

            HistoryRecords = records
                .Select(r => new HistoryRecordViewModel(r))
                .ToList();

            _historyLoaded = true;
        }
        catch
        {
            // History is best-effort — show empty state rather than crashing.
            HistoryRecords = [];
        }
        finally
        {
            _isHistoryLoading = false;
            OnPropertyChanged(nameof(HistoryLoadingVisibility));
            OnPropertyChanged(nameof(HistoryRecords));
            OnPropertyChanged(nameof(HasHistory));
            OnPropertyChanged(nameof(HasNoHistory));
            OnPropertyChanged(nameof(HistoryListVisibility));
            OnPropertyChanged(nameof(HistoryEmptyVisibility));
            OnPropertyChanged(nameof(HistoryCountText));
        }
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Unsubscribes from the app-level intelligence service.
    /// Call from InsightsPage.Unloaded.
    /// </summary>
    public void Cleanup() =>
        AppServices.Intelligence.InsightsUpdated -= OnInsightsUpdated;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Visibility Vis(bool show) =>
        show ? Visibility.Visible : Visibility.Collapsed;
}
