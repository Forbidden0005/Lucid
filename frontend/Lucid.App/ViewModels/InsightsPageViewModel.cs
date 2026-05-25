using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Intelligence;
using Lucid.Services.Learning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lucid.ViewModels;

// ── Tab / filter enums ────────────────────────────────────────────────────────

/// <summary>
/// The five content tabs of the Intelligence Workspace.
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

    /// <summary>Full ranked list of recommended actions across all active findings.</summary>
    Actions    = 4,

    /// <summary>Synthesized early warnings from anomalies and Watchtower alerts.</summary>
    Warnings   = 5,

    /// <summary>Short-term behavioral anomalies detected against the machine baseline.</summary>
    Anomalies  = 6,

    /// <summary>Long-term operational drift observations for CPU, RAM, and Disk.</summary>
    Drift      = 7,

    /// <summary>Adaptive personalization — user style, intervention history, effectiveness summaries.</summary>
    Personalization = 8,
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
    [NotifyPropertyChangedFor(nameof(IsActionsTab))]
    [NotifyPropertyChangedFor(nameof(IsWarningsTab))]
    [NotifyPropertyChangedFor(nameof(IsAnomaliesTab))]
    [NotifyPropertyChangedFor(nameof(IsDriftTab))]
    [NotifyPropertyChangedFor(nameof(AllTabIndicatorVisibility))]
    [NotifyPropertyChangedFor(nameof(TrendsTabIndicatorVisibility))]
    [NotifyPropertyChangedFor(nameof(CorrelatedTabIndicatorVisibility))]
    [NotifyPropertyChangedFor(nameof(HistoryTabIndicatorVisibility))]
    [NotifyPropertyChangedFor(nameof(ActionsTabIndicatorVisibility))]
    [NotifyPropertyChangedFor(nameof(WarningsTabIndicatorVisibility))]
    [NotifyPropertyChangedFor(nameof(AnomaliesTabIndicatorVisibility))]
    [NotifyPropertyChangedFor(nameof(DriftTabIndicatorVisibility))]
    [NotifyPropertyChangedFor(nameof(PersonalizationTabIndicatorVisibility))]
    [NotifyPropertyChangedFor(nameof(AllTabBrush))]
    [NotifyPropertyChangedFor(nameof(TrendsTabBrush))]
    [NotifyPropertyChangedFor(nameof(CorrelatedTabBrush))]
    [NotifyPropertyChangedFor(nameof(HistoryTabBrush))]
    [NotifyPropertyChangedFor(nameof(ActionsTabBrush))]
    [NotifyPropertyChangedFor(nameof(WarningsTabBrush))]
    [NotifyPropertyChangedFor(nameof(AnomaliesTabBrush))]
    [NotifyPropertyChangedFor(nameof(DriftTabBrush))]
    [NotifyPropertyChangedFor(nameof(PersonalizationTabBrush))]
    [NotifyPropertyChangedFor(nameof(FindingsPanelVisibility))]
    [NotifyPropertyChangedFor(nameof(HistoryPanelVisibility))]
    [NotifyPropertyChangedFor(nameof(ActionsPanelVisibility))]
    [NotifyPropertyChangedFor(nameof(WarningsPanelVisibility))]
    [NotifyPropertyChangedFor(nameof(AnomaliesPanelVisibility))]
    [NotifyPropertyChangedFor(nameof(DriftPanelVisibility))]
    [NotifyPropertyChangedFor(nameof(PersonalizationPanelVisibility))]
    [NotifyPropertyChangedFor(nameof(FilterBarVisibility))]
    [NotifyPropertyChangedFor(nameof(NoResultsVisibility))]
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

    public bool IsAllTab             => ActiveTab == InsightTab.All;
    public bool IsTrendsTab          => ActiveTab == InsightTab.Trends;
    public bool IsCorrelatedTab      => ActiveTab == InsightTab.Correlated;
    public bool IsHistoryTab         => ActiveTab == InsightTab.History;
    public bool IsActionsTab         => ActiveTab == InsightTab.Actions;
    public bool IsWarningsTab        => ActiveTab == InsightTab.Warnings;
    public bool IsAnomaliesTab       => ActiveTab == InsightTab.Anomalies;
    public bool IsDriftTab           => ActiveTab == InsightTab.Drift;
    public bool IsPersonalizationTab => ActiveTab == InsightTab.Personalization;

    public Visibility AllTabIndicatorVisibility             => Vis(IsAllTab);
    public Visibility TrendsTabIndicatorVisibility          => Vis(IsTrendsTab);
    public Visibility CorrelatedTabIndicatorVisibility      => Vis(IsCorrelatedTab);
    public Visibility HistoryTabIndicatorVisibility         => Vis(IsHistoryTab);
    public Visibility ActionsTabIndicatorVisibility         => Vis(IsActionsTab);
    public Visibility WarningsTabIndicatorVisibility        => Vis(IsWarningsTab);
    public Visibility AnomaliesTabIndicatorVisibility       => Vis(IsAnomaliesTab);
    public Visibility DriftTabIndicatorVisibility           => Vis(IsDriftTab);
    public Visibility PersonalizationTabIndicatorVisibility => Vis(IsPersonalizationTab);

    public SolidColorBrush AllTabBrush             => IsAllTab             ? s_tabActiveBrush : s_tabInactiveBrush;
    public SolidColorBrush TrendsTabBrush          => IsTrendsTab          ? s_tabActiveBrush : s_tabInactiveBrush;
    public SolidColorBrush CorrelatedTabBrush      => IsCorrelatedTab      ? s_tabActiveBrush : s_tabInactiveBrush;
    public SolidColorBrush HistoryTabBrush         => IsHistoryTab         ? s_tabActiveBrush : s_tabInactiveBrush;
    public SolidColorBrush ActionsTabBrush         => IsActionsTab         ? s_tabActiveBrush : s_tabInactiveBrush;
    public SolidColorBrush WarningsTabBrush        => IsWarningsTab        ? s_tabActiveBrush : s_tabInactiveBrush;
    public SolidColorBrush AnomaliesTabBrush       => IsAnomaliesTab       ? s_tabActiveBrush : s_tabInactiveBrush;
    public SolidColorBrush DriftTabBrush           => IsDriftTab           ? s_tabActiveBrush : s_tabInactiveBrush;
    public SolidColorBrush PersonalizationTabBrush => IsPersonalizationTab ? s_tabActiveBrush : s_tabInactiveBrush;

    // Findings panel: visible for All / Trends / Correlated tabs only
    private bool IsAnomalyIntelligenceTab =>
        IsWarningsTab || IsAnomaliesTab || IsDriftTab;

    private bool IsSpecialTab =>
        IsHistoryTab || IsActionsTab || IsAnomalyIntelligenceTab || IsPersonalizationTab;

    public Visibility FindingsPanelVisibility        => Vis(!IsSpecialTab);
    public Visibility HistoryPanelVisibility         => Vis(IsHistoryTab);
    public Visibility ActionsPanelVisibility         => Vis(IsActionsTab);
    public Visibility WarningsPanelVisibility        => Vis(IsWarningsTab);
    public Visibility AnomaliesPanelVisibility       => Vis(IsAnomaliesTab);
    public Visibility DriftPanelVisibility           => Vis(IsDriftTab);
    public Visibility PersonalizationPanelVisibility => Vis(IsPersonalizationTab);

    // Filter + sort bar: hidden for special tabs
    public Visibility FilterBarVisibility => Vis(!IsSpecialTab);

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
    public Visibility NoResultsVisibility    => Vis(!HasDisplayedInsights && !IsSpecialTab);

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

    // ── Actions tab — ranked recommended actions ──────────────────────────────

    /// <summary>
    /// Shared stateless prioritizer — reused across re-evaluations.
    /// </summary>
    private static readonly GlobalRecommendationPrioritizer s_prioritizer = new();

    /// <summary>
    /// Full ranked list of recommended actions from all active insights,
    /// ordered by priority score descending (highest predicted benefit first).
    /// Rebuilt on every InsightsUpdated event — always up to date when
    /// the user switches to the Actions tab.
    /// </summary>
    public IReadOnlyList<PrioritizedActionViewModel> DisplayedActions { get; private set; } = [];

    public bool HasDisplayedActions  => DisplayedActions.Count > 0;

    public Visibility ActionsListVisibility  => Vis(HasDisplayedActions);
    public Visibility ActionsEmptyVisibility => Vis(!HasDisplayedActions);

    public string ActionsCountText =>
        $"{DisplayedActions.Count} action{(DisplayedActions.Count == 1 ? "" : "s")}";

    // ── Warnings tab ─────────────────────────────────────────────────────────

    /// <summary>
    /// Synthesized early warnings from anomalies and Watchtower alerts.
    /// Updated live via EarlyWarningService.WarningsUpdated.
    /// </summary>
    public IReadOnlyList<EarlyWarning> DisplayedWarnings { get; private set; } = [];

    public bool HasDisplayedWarnings   => DisplayedWarnings.Count > 0;
    public Visibility WarningsListVisibility  => Vis(HasDisplayedWarnings);
    public Visibility WarningsEmptyVisibility => Vis(!HasDisplayedWarnings);
    public string WarningsCountText =>
        $"{DisplayedWarnings.Count} warning{(DisplayedWarnings.Count == 1 ? "" : "s")}";

    // ── Anomalies tab ─────────────────────────────────────────────────────────

    /// <summary>
    /// Short-term behavioral anomalies detected against the machine baseline.
    /// Updated live via AnomalyDetectionService.AnomaliesUpdated.
    /// </summary>
    public IReadOnlyList<DetectedAnomaly> DisplayedAnomalies { get; private set; } = [];

    public bool HasDisplayedAnomalies    => DisplayedAnomalies.Count > 0;
    public Visibility AnomaliesListVisibility  => Vis(HasDisplayedAnomalies);
    public Visibility AnomaliesEmptyVisibility => Vis(!HasDisplayedAnomalies);
    public string AnomaliesCountText =>
        $"{DisplayedAnomalies.Count} anomal{(DisplayedAnomalies.Count == 1 ? "y" : "ies")}";

    // ── Drift tab ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Long-term operational drift observations adapted from the Watchtower layer.
    /// Updated live via DriftDetectionService.DriftsUpdated.
    /// </summary>
    public IReadOnlyList<DetectedDrift> DisplayedDrifts { get; private set; } = [];

    public bool HasDisplayedDrifts    => DisplayedDrifts.Count > 0;
    public Visibility DriftsListVisibility  => Vis(HasDisplayedDrifts);
    public Visibility DriftsEmptyVisibility => Vis(!HasDisplayedDrifts);
    public string DriftsCountText =>
        $"{DisplayedDrifts.Count} metric{(DisplayedDrifts.Count == 1 ? "" : "s")} tracked";

    // ── Personalization tab ───────────────────────────────────────────────────

    /// <summary>Personalization profile for this session.</summary>
    private PersonalizationProfile? _personalizationProfile;

    /// <summary>Operational style report. Null until tab first opened.</summary>
    public OperationalStyleReport? PersonalizationStyleReport { get; private set; }

    /// <summary>Recent intervention records for display in the tab.</summary>
    public IReadOnlyList<InterventionRecord> RecentInterventions { get; private set; } = [];

    public bool HasPersonalizationData     => _personalizationProfile?.IsWarmEnough == true;
    public bool HasNoPersonalizationData   => !HasPersonalizationData;

    public Visibility PersonalizationDataVisibility  => Vis(HasPersonalizationData);
    public Visibility PersonalizationColdVisibility  => Vis(HasNoPersonalizationData);
    public Visibility PersonalizationInterventionListVisibility => Vis(RecentInterventions.Count > 0);

    // Flat accessors for OperationalStyleReport (nullable-safe for x:Bind)
    public string PersonalizationStyleLabel   => PersonalizationStyleReport?.StyleLabel   ?? "Learning your style";
    public string PersonalizationStyleDesc    => PersonalizationStyleReport?.StyleDescription ?? "";
    public string PersonalizationStyleGlyph   => PersonalizationStyleReport?.StyleGlyph   ?? "";
    public string PersonalizationStyleColor   => PersonalizationStyleReport?.StyleColor   ?? "#6B7280";
    public string PersonalizationStyleInsight => PersonalizationStyleReport?.PersonalInsight ?? "";
    public string PersonalizationSampleCount  =>
        $"Based on {_personalizationProfile?.TotalRecords ?? 0} recorded interaction" +
        $"{(_personalizationProfile?.TotalRecords == 1 ? "" : "s")}";

    public string PersonalizationAcceptanceRate =>
        _personalizationProfile is null ? "—" :
        $"{_personalizationProfile.AcceptanceRate:0%} overall acceptance rate";

    /// <summary>Formatted category acceptance rates for display in the tab.</summary>
    public IReadOnlyList<string> CategoryRateLines { get; private set; } = [];

    // ── Card cache ────────────────────────────────────────────────────────────

    /// <summary>
    /// Preserves IsExpanded state across engine re-evaluations.
    /// Same pattern as DashboardViewModel._cardCache.
    /// </summary>
    private readonly Dictionary<string, InsightCardViewModel> _cardCache = new();

    // ── Navigation callback ───────────────────────────────────────────────────

    private Action<string>? _navigateToDetail;

    /// <summary>
    /// Registers the callback used by insight cards to navigate to the
    /// InsightDetailPage. Call once from InsightsPage code-behind after
    /// ViewModel construction so all current and future cards are wired.
    /// </summary>
    public void SetNavigationCallback(Action<string> callback)
    {
        _navigateToDetail = callback;
        foreach (var card in _cardCache.Values)
            card.RequestNavigateToDetail = callback;
    }

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

        // Subscribe to anomaly intelligence services (all UI-thread events).
        AppServices.EarlyWarning.WarningsUpdated      += OnWarningsUpdated;
        AppServices.AnomalyDetection.AnomaliesUpdated += OnAnomaliesUpdated;
        AppServices.DriftDetection.DriftsUpdated      += OnDriftsUpdated;

        // Seed from current state.
        ApplyWarnings(AppServices.EarlyWarning.CurrentWarnings);
        ApplyAnomalies(AppServices.AnomalyDetection.CurrentAnomalies);
        ApplyDrifts(AppServices.DriftDetection.CurrentDrifts);
    }

    // ── CommunityToolkit partial hooks ────────────────────────────────────────

    partial void OnActiveTabChanged(InsightTab value)
    {
        if (value == InsightTab.History)
            _ = LoadHistoryAsync(force: false);
        else if (value == InsightTab.Personalization)
            LoadPersonalizationData();
        else if (value is InsightTab.Actions or InsightTab.Warnings or InsightTab.Anomalies or InsightTab.Drift)
        {
            // These tabs maintain their data independently — no findings rebuild needed.
        }
        else
            RebuildDisplay();
    }

    partial void OnActiveFilterChanged(InsightFilter value)
    {
        if (!IsSpecialTab)
            RebuildDisplay();
    }

    partial void OnActiveSortIndexChanged(int value)
    {
        if (!IsSpecialTab)
            RebuildDisplay();
    }

    /// <summary>
    /// Loads personalization data for the Personalization tab.
    /// Synchronous — reads from in-memory service collections.
    /// </summary>
    private void LoadPersonalizationData()
    {
        try
        {
            var records = AppServices.InterventionMemory.Records;
            _personalizationProfile = AppServices.PersonalizationEngine.ComputeProfile(records);
            PersonalizationStyleReport = AppServices.UserBehaviorClassifier.Classify(_personalizationProfile);
            RecentInterventions = records.Take(15).ToList().AsReadOnly();

            // Format category rates for display
            CategoryRateLines = _personalizationProfile.CategoryAcceptanceRates
                .OrderByDescending(kv => kv.Value)
                .Select(kv => $"{FormatCategory(kv.Key)}: {kv.Value:0%} acceptance")
                .ToList()
                .AsReadOnly();
        }
        catch
        {
            _personalizationProfile    = null;
            PersonalizationStyleReport = null;
            RecentInterventions        = [];
            CategoryRateLines          = [];
        }

        // Notify all personalization properties
        OnPropertyChanged(nameof(PersonalizationStyleReport));
        OnPropertyChanged(nameof(RecentInterventions));
        OnPropertyChanged(nameof(HasPersonalizationData));
        OnPropertyChanged(nameof(HasNoPersonalizationData));
        OnPropertyChanged(nameof(PersonalizationDataVisibility));
        OnPropertyChanged(nameof(PersonalizationColdVisibility));
        OnPropertyChanged(nameof(PersonalizationInterventionListVisibility));
        OnPropertyChanged(nameof(PersonalizationStyleLabel));
        OnPropertyChanged(nameof(PersonalizationStyleDesc));
        OnPropertyChanged(nameof(PersonalizationStyleGlyph));
        OnPropertyChanged(nameof(PersonalizationStyleColor));
        OnPropertyChanged(nameof(PersonalizationStyleInsight));
        OnPropertyChanged(nameof(PersonalizationSampleCount));
        OnPropertyChanged(nameof(PersonalizationAcceptanceRate));
        OnPropertyChanged(nameof(CategoryRateLines));
    }

    private static string FormatCategory(string category) => category.ToLowerInvariant() switch
    {
        "disk"    => "Disk cleanup",
        "startup" => "Startup",
        "process" => "Process",
        "repair"  => "Repair",
        "storage" => "Storage",
        "network" => "Network",
        "cpu"     => "CPU",
        "ram"     => "Memory",
        "security" => "Security",
        _ => char.ToUpperInvariant(category[0]) + category[1..],
    };

    // ── Tab commands ──────────────────────────────────────────────────────────

    [RelayCommand] private void SelectTabAll()             => ActiveTab = InsightTab.All;
    [RelayCommand] private void SelectTabTrends()          => ActiveTab = InsightTab.Trends;
    [RelayCommand] private void SelectTabCorrelated()      => ActiveTab = InsightTab.Correlated;
    [RelayCommand] private void SelectTabHistory()         => ActiveTab = InsightTab.History;
    [RelayCommand] private void SelectTabActions()         => ActiveTab = InsightTab.Actions;
    [RelayCommand] private void SelectTabWarnings()        => ActiveTab = InsightTab.Warnings;
    [RelayCommand] private void SelectTabAnomalies()       => ActiveTab = InsightTab.Anomalies;
    [RelayCommand] private void SelectTabDrift()           => ActiveTab = InsightTab.Drift;
    [RelayCommand] private void SelectTabPersonalization() => ActiveTab = InsightTab.Personalization;

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
                _cardCache[insight.Id] = new InsightCardViewModel(insight)
                {
                    RequestNavigateToDetail = _navigateToDetail,
                };
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

        // Rebuild actions list regardless of active tab — data is always fresh.
        RebuildActions(insights);

        if (!IsSpecialTab)
            RebuildDisplay();
    }

    // ── Actions rebuild ───────────────────────────────────────────────────────

    /// <summary>
    /// Ranks all recommended actions from the active insight set and publishes
    /// the result as <see cref="DisplayedActions"/>. Always called on every
    /// InsightsUpdated event so the Actions tab is instantly current on switch.
    /// </summary>
    private void RebuildActions(IReadOnlyList<SystemInsight> insights)
    {
        var ranked = s_prioritizer.Rank(
            insights,
            AppServices.LearningService,
            _personalizationProfile,
            AppServices.AlertFatigueManager,
            AppServices.RecommendationExplanation);
        DisplayedActions = ranked.Select(PrioritizedActionViewModel.From).ToList();

        OnPropertyChanged(nameof(DisplayedActions));
        OnPropertyChanged(nameof(HasDisplayedActions));
        OnPropertyChanged(nameof(ActionsListVisibility));
        OnPropertyChanged(nameof(ActionsEmptyVisibility));
        OnPropertyChanged(nameof(ActionsCountText));
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

    // ── Anomaly intelligence feed ─────────────────────────────────────────────

    private void OnWarningsUpdated(object? sender, IReadOnlyList<EarlyWarning> warnings) =>
        ApplyWarnings(warnings);

    private void ApplyWarnings(IReadOnlyList<EarlyWarning> warnings)
    {
        DisplayedWarnings = warnings;
        OnPropertyChanged(nameof(DisplayedWarnings));
        OnPropertyChanged(nameof(HasDisplayedWarnings));
        OnPropertyChanged(nameof(WarningsListVisibility));
        OnPropertyChanged(nameof(WarningsEmptyVisibility));
        OnPropertyChanged(nameof(WarningsCountText));
    }

    private void OnAnomaliesUpdated(object? sender, IReadOnlyList<DetectedAnomaly> anomalies) =>
        ApplyAnomalies(anomalies);

    private void ApplyAnomalies(IReadOnlyList<DetectedAnomaly> anomalies)
    {
        DisplayedAnomalies = anomalies;
        OnPropertyChanged(nameof(DisplayedAnomalies));
        OnPropertyChanged(nameof(HasDisplayedAnomalies));
        OnPropertyChanged(nameof(AnomaliesListVisibility));
        OnPropertyChanged(nameof(AnomaliesEmptyVisibility));
        OnPropertyChanged(nameof(AnomaliesCountText));
    }

    private void OnDriftsUpdated(object? sender, IReadOnlyList<DetectedDrift> drifts) =>
        ApplyDrifts(drifts);

    private void ApplyDrifts(IReadOnlyList<DetectedDrift> drifts)
    {
        DisplayedDrifts = drifts;
        OnPropertyChanged(nameof(DisplayedDrifts));
        OnPropertyChanged(nameof(HasDisplayedDrifts));
        OnPropertyChanged(nameof(DriftsListVisibility));
        OnPropertyChanged(nameof(DriftsEmptyVisibility));
        OnPropertyChanged(nameof(DriftsCountText));
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Unsubscribes from the app-level intelligence service.
    /// Call from InsightsPage.Unloaded.
    /// </summary>
    public void Cleanup()
    {
        AppServices.Intelligence.InsightsUpdated      -= OnInsightsUpdated;
        AppServices.EarlyWarning.WarningsUpdated      -= OnWarningsUpdated;
        AppServices.AnomalyDetection.AnomaliesUpdated -= OnAnomaliesUpdated;
        AppServices.DriftDetection.DriftsUpdated      -= OnDriftsUpdated;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Visibility Vis(bool show) =>
        show ? Visibility.Visible : Visibility.Collapsed;
}
