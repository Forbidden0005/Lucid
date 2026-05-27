using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Intelligence;
using Lucid.Services.Simulation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Lucid.ViewModels;

// ── Sub-ViewModels ─────────────────────────────────────────────────────────────

/// <summary>ViewModel for a single scenario type selector chip.</summary>
public sealed partial class ScenarioTypeChipViewModel : ObservableObject
{
    [ObservableProperty]
    private SimulationScenarioType _scenarioType;

    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _glyph = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedBorderBrush))]
    private bool _isSelected;

    public string SelectedBorderBrush => IsSelected ? "#60A5FA" : "Transparent";
}

/// <summary>ViewModel for a single projected metric row in the outcome card.</summary>
public sealed partial class ProjectedMetricViewModel : ObservableObject
{
    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private string _currentValue = "";

    [ObservableProperty]
    private string _projectedValue = "";

    [ObservableProperty]
    private string _delta = "";

    [ObservableProperty]
    private string _deltaColor = "#6B7280";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeltaVisibility))]
    private bool _hasDelta;

    public Visibility DeltaVisibility => HasDelta ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>ViewModel for a single branch outcome (WithAction / WithoutAction).</summary>
public sealed partial class BranchOutcomeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _narrative = "";

    [ObservableProperty]
    private string _stabilityPercent = "";

    [ObservableProperty]
    private string _reliefDays = "";

    [ObservableProperty]
    private string _startupImpact = "";

    [ObservableProperty]
    private string _accentColor = "#6B7280";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReliefDaysVisibility))]
    private bool _hasReliefDays;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartupImpactVisibility))]
    private bool _hasStartupImpact;

    public Visibility ReliefDaysVisibility   => HasReliefDays    ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StartupImpactVisibility => HasStartupImpact ? Visibility.Visible : Visibility.Collapsed;

    public ObservableCollection<ProjectedMetricViewModel> Metrics { get; } = [];
    public ObservableCollection<string>                   KeyInsights { get; } = [];
}

/// <summary>ViewModel for a single trajectory chart bar (visual trajectory point).</summary>
public sealed partial class TrajectoryPointViewModel : ObservableObject
{
    [ObservableProperty]
    private string _timeLabel = "";

    [ObservableProperty]
    private double _withActionValue;

    [ObservableProperty]
    private double _withoutActionValue;

    [ObservableProperty]
    private string _withActionColor = "#60A5FA";

    [ObservableProperty]
    private string _withoutActionColor = "#6B7280";

    public string WithActionLabel    => $"{WithActionValue:F0}%";
    public string WithoutActionLabel => $"{WithoutActionValue:F0}%";
}

/// <summary>ViewModel for a single risk factor row.</summary>
public sealed partial class RiskFactorViewModel : ObservableObject
{
    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private bool _isWarning;
}

/// <summary>ViewModel for a single historical basis entry.</summary>
public sealed partial class HistoricalBasisEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _dataSource = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _contribution = "";
}

/// <summary>
/// An intelligence context signal displayed in the trajectory chart's overlay panel.
/// Immutable — built fresh each time anomaly/drift/warning state changes.
/// </summary>
public sealed record IntelContextItem(
    string Glyph,
    string Color,
    string BadgeColor,
    string Label,
    string Description);

/// <summary>
/// ViewModel for a single quick-pick scenario preset chip.
/// Immutable — preset configs do not change during the session.
/// </summary>
public sealed class ScenarioPresetViewModel
{
    public string                 Name         { get; init; } = "";
    public string                 Description  { get; init; } = "";
    public string                 Glyph        { get; init; } = "";
    public SimulationScenarioType ScenarioType { get; init; }
    public int                    HorizonIndex { get; init; }
}

/// <summary>
/// ViewModel for a persisted simulation snapshot in the history card.
/// Immutable — snapshot data does not change after capture.
/// </summary>
public sealed class SimulationSnapshotViewModel
{
    public string     ScenarioLabel       { get; init; } = "";
    public string     HeadlineVerdict     { get; init; } = "";
    public string     DecisionLabel       { get; init; } = "";
    public string     DecisionColor       { get; init; } = "#6B7280";
    public string     ConfidenceTierLabel { get; init; } = "";
    public string     ConfidenceTierColor { get; init; } = "#6B7280";
    public string     TimeAgo             { get; init; } = "";
    public string     RamDeltaLabel       { get; init; } = "";
    public string     RamDeltaColor       { get; init; } = "#6B7280";
    public string     CpuDeltaLabel       { get; init; } = "";
    public string     CpuDeltaColor       { get; init; } = "#6B7280";

    // ── Outcome accuracy (populated once the measurement window elapses) ───────
    public string     AccuracyLabel      { get; init; } = "";
    public string     AccuracyColor      { get; init; } = "#6B7280";
    public bool       HasAccuracy        { get; init; } = false;
    public Visibility AccuracyVisibility => HasAccuracy ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// ViewModel for a single row in the "Compare All Presets" side-by-side table.
/// Immutable — populated from a single batch comparison run.
/// </summary>
public sealed class ComparisonRowViewModel
{
    public string ScenarioLabel   { get; init; } = "";
    public string RamChange       { get; init; } = "";
    public string RamColor        { get; init; } = "#6B7280";
    public string CpuChange       { get; init; } = "";
    public string CpuColor        { get; init; } = "#6B7280";
    public string RiskLabel       { get; init; } = "";
    public string RiskColor       { get; init; } = "#6B7280";
    public string ConfidenceLabel { get; init; } = "";
    public string HorizonLabel    { get; init; } = "";
}

// ── Main ViewModel ─────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for the Operational Simulation ("What If?") page.
///
/// Exposes:
///   • Scenario type selector chips
///   • Simulation horizon selector
///   • Confidence score breakdown
///   • WithAction vs WithoutAction branch outcomes
///   • Projected metric deltas
///   • Branch trajectory visualization points
///   • Operational risk projection
///   • Historical basis entries
/// </summary>
public sealed partial class SimulationViewModel : ObservableObject
{
    private readonly OperationalSimulationEngine     _engine;
    private readonly ISimulationHistoryService       _historyService;
    private readonly IOutcomeVerificationService     _outcomeVerification;
    private CancellationTokenSource? _activeCts;

    // ── Scenario selection ────────────────────────────────────────────────────
    public ObservableCollection<ScenarioTypeChipViewModel> ScenarioChips { get; } = [];

    private SimulationScenarioType _selectedScenario = SimulationScenarioType.RestartSystem;

    // ── State ─────────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SimulatingVisibility))]
    private bool _isSimulating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultVisibility))]
    private bool _hasResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InsufficientDataVisibility))]
    private bool _insufficientData;

    public Visibility SimulatingVisibility        => IsSimulating     ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ResultVisibility            => HasResult        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InsufficientDataVisibility  => InsufficientData ? Visibility.Visible : Visibility.Collapsed;

    // ── Horizon display ───────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HorizonLabel))]
    private int _horizonIndex = 2; // default: 4 hours

    public string HorizonLabel => HorizonIndex switch
    {
        0 => "15 minutes",
        1 => "1 hour",
        2 => "4 hours",
        3 => "24 hours",
        4 => "7 days",
        _ => "4 hours",
    };

    // ── Scenario narrative ────────────────────────────────────────────────────
    [ObservableProperty]
    private string _scenarioNarrative = "";

    [ObservableProperty]
    private string _simulatedAt = "";

    // ── Confidence ────────────────────────────────────────────────────────────
    [ObservableProperty]
    private string _confidencePercent = "";

    [ObservableProperty]
    private string _confidenceLabel = "";

    [ObservableProperty]
    private string _confidenceExplanation = "";

    [ObservableProperty]
    private string _confidenceColor = "#6B7280";

    [ObservableProperty]
    private double _confidenceValue;

    public ObservableCollection<string> ConfidenceFactors { get; } = [];

    // ── Branch outcomes ───────────────────────────────────────────────────────
    [ObservableProperty]
    private BranchOutcomeViewModel _withAction = new();

    [ObservableProperty]
    private BranchOutcomeViewModel _withoutAction = new();

    // ── Risk ──────────────────────────────────────────────────────────────────
    [ObservableProperty]
    private string _riskLevel = "";

    [ObservableProperty]
    private string _riskColor = "#6B7280";

    [ObservableProperty]
    private string _riskGlyph = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReversibleBadgeVisibility))]
    private bool _isReversible;

    [ObservableProperty]
    private string _recurrencePercent = "";

    [ObservableProperty]
    private string _rollbackPercent = "";

    public Visibility ReversibleBadgeVisibility =>
        IsReversible ? Visibility.Visible : Visibility.Collapsed;

    public ObservableCollection<RiskFactorViewModel>       RiskFactors    { get; } = [];
    public ObservableCollection<string>                    Mitigations    { get; } = [];

    // ── Historical basis ──────────────────────────────────────────────────────
    public ObservableCollection<HistoricalBasisEntryViewModel> HistoricalBasis { get; } = [];

    // ── Trajectory visualization ──────────────────────────────────────────────
    public ObservableCollection<TrajectoryPointViewModel> TrajectoryPoints { get; } = [];

    // Chart polyline data — PointCollection is a DependencyObjectCollection;
    // mutating it in-place triggers VectorChanged on the bound Polyline without
    // needing to replace the reference.  Never replace these instances.
    public PointCollection WithActionPoints    { get; } = new();
    public PointCollection WithoutActionPoints { get; } = new();

    // ── Scenario presets ──────────────────────────────────────────────────────
    public ObservableCollection<ScenarioPresetViewModel> ScenarioPresets { get; } = [];

    // ── Simulation history ────────────────────────────────────────────────────
    public ObservableCollection<SimulationSnapshotViewModel> SnapshotHistory { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HistoryVisibility))]
    private bool _hasHistory;

    public Visibility HistoryVisibility =>
        HasHistory ? Visibility.Visible : Visibility.Collapsed;

    // ── Scenario comparison ───────────────────────────────────────────────────
    public ObservableCollection<ComparisonRowViewModel> ComparisonRows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ComparisonVisibility))]
    private bool _hasComparison;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ComparingVisibility))]
    private bool _isComparing;

    public Visibility ComparisonVisibility =>
        HasComparison ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ComparingVisibility =>
        IsComparing ? Visibility.Visible : Visibility.Collapsed;

    // ── Decision card ─────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DecisionCardVisibility))]
    private bool _hasDecision;

    [ObservableProperty]
    private string _headlineVerdict = "";

    [ObservableProperty]
    private string _decisionLabel = "";

    [ObservableProperty]
    private string _decisionColor = "#6B7280";

    [ObservableProperty]
    private string _urgencyLabel = "";

    [ObservableProperty]
    private string _urgencyColor = "#6B7280";

    [ObservableProperty]
    private string _projectedGainSummary = "";

    [ObservableProperty]
    private string _divergenceSummary = "";

    /// <summary>Tier badge text: "High Confidence" / "Moderate Confidence" / etc.</summary>
    [ObservableProperty]
    private string _confidenceTierLabel = "";

    /// <summary>Accent color for the confidence tier badge.</summary>
    [ObservableProperty]
    private string _confidenceTierColor = "#6B7280";

    public Visibility DecisionCardVisibility =>
        HasDecision ? Visibility.Visible : Visibility.Collapsed;

    // ── Anomaly intelligence context (chart overlays + contextual panel) ──────
    //
    // The trajectory chart Canvas is 560 × 130 px.  RAM 0–100% maps to
    // Y 130–0 px (inverted).  The anomaly dot is positioned at the live RAM
    // reading so it marks where the machine currently sits on the chart.
    //
    // These properties are refreshed any time the anomaly/drift/warning sets
    // change (via service events) AND after each simulation result is applied
    // (so the confidence band updates with the new confidence value).
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>True when live anomalies exist that may affect this projection.</summary>
    public bool HasAnomalyContext { get; private set; }

    /// <summary>True when active drift observations exist.</summary>
    public bool HasDriftContext { get; private set; }

    /// <summary>True when active early warnings exist.</summary>
    public bool HasWarningContext { get; private set; }

    /// <summary>True when any intelligence signal is present.</summary>
    public bool HasIntelligenceContext => HasAnomalyContext || HasDriftContext || HasWarningContext;

    // Chart overlay Visibility properties
    public Visibility AnomalyDotVisibility          => HasAnomalyContext      ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DriftOverlayVisibility        => HasDriftContext         ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WarningFlagVisibility         => HasWarningContext       ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IntelligenceContextVisibility => HasIntelligenceContext  ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ConfidenceBandVisibility      => HasResult              ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Canvas Y coordinate for the anomaly epoch dot (current RAM level).</summary>
    public double AnomalyDotY { get; private set; } = 65.0;

    /// <summary>Canvas Y coordinate for the top edge of the confidence band rectangle.</summary>
    public double ConfidenceBandTop { get; private set; } = 32.0;

    /// <summary>Height of the confidence band rectangle in canvas pixels.</summary>
    public double ConfidenceBandHeight { get; private set; } = 33.0;

    /// <summary>Ordered list of active intelligence signals (anomalies → drifts → warnings).</summary>
    public IReadOnlyList<IntelContextItem> IntelligenceContextItems { get; private set; } = [];

    /// <summary>Summary label: "N active intelligence signals may affect this projection".</summary>
    public string IntelligenceContextLabel { get; private set; } = "";

    // ── Constructor ───────────────────────────────────────────────────────────

    public SimulationViewModel(
        OperationalSimulationEngine engine,
        ISimulationHistoryService   historyService,
        IOutcomeVerificationService outcomeVerification)
    {
        _engine              = engine;
        _historyService      = historyService;
        _outcomeVerification = outcomeVerification;

        BuildScenarioChips();
        BuildScenarioPresets();

        // Evaluate any pending outcome verifications from the current session
        // before building the history — some may have matured since the last visit.
        EvaluatePending();
        RefreshHistory();

        // Subscribe to anomaly intelligence services — keep chart overlays live.
        AppServices.AnomalyDetection.AnomaliesUpdated += OnAnomaliesUpdated;
        AppServices.DriftDetection.DriftsUpdated       += OnDriftsUpdated;
        AppServices.EarlyWarning.WarningsUpdated        += OnWarningsUpdated;
        RefreshIntelligenceContext();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RunSimulationAsync()
    {
        // Evaluate pending outcome verifications before starting a new run —
        // any simulations from 30+ minutes ago in this session can now be assessed.
        EvaluatePending();

        // Cancel any in-progress simulation
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _activeCts = new CancellationTokenSource();
        var ct = _activeCts.Token;

        IsSimulating = true;
        HasResult    = false;

        try
        {
            var input = new SimulationInput(
                ScenarioType: _selectedScenario,
                Horizon:      IndexToHorizon(HorizonIndex));

            var result = await _engine.SimulateAsync(input, ct).ConfigureAwait(true);

            if (!ct.IsCancellationRequested)
                ApplyResult(result);
        }
        catch (OperationCanceledException)
        {
            // User triggered a new simulation — ignore
        }
        finally
        {
            IsSimulating = false;
        }
    }

    [RelayCommand]
    private async Task SelectScenarioAsync(SimulationScenarioType scenario)
    {
        _selectedScenario = scenario;

        foreach (var chip in ScenarioChips)
            chip.IsSelected = chip.ScenarioType == scenario;

        // Auto-run simulation when scenario changes
        await RunSimulationAsync();
    }

    [RelayCommand]
    private async Task SelectPresetAsync(ScenarioPresetViewModel preset)
    {
        _selectedScenario = preset.ScenarioType;
        HorizonIndex      = preset.HorizonIndex;

        foreach (var chip in ScenarioChips)
            chip.IsSelected = chip.ScenarioType == preset.ScenarioType;

        // Auto-run when a preset is tapped
        await RunSimulationAsync();
    }

    [RelayCommand]
    private async Task RunComparisonAsync()
    {
        if (IsComparing) return;

        IsComparing   = true;
        HasComparison = false;
        ComparisonRows.Clear();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

            foreach (var preset in SimulationPresetLibrary.All)
            {
                var input = new SimulationInput(
                    ScenarioType: preset.ScenarioType,
                    Horizon:      preset.Horizon);

                var result   = await _engine.SimulateAsync(input, cts.Token).ConfigureAwait(true);
                var decision = SimulationDecisionEngine.Derive(result);
                var with     = result.WithActionBranch?.ProjectedState;

                ComparisonRows.Add(new ComparisonRowViewModel
                {
                    ScenarioLabel   = preset.Name,
                    RamChange       = with is null ? "—" : FormatMetricDelta(with.RamDelta),
                    RamColor        = with is null ? "#6B7280" : MetricDeltaColor(with.RamDelta),
                    CpuChange       = with is null ? "—" : FormatMetricDelta(with.CpuDelta),
                    CpuColor        = with is null ? "#6B7280" : MetricDeltaColor(with.CpuDelta),
                    RiskLabel       = decision.DecisionLabel,
                    RiskColor       = decision.DecisionColor,
                    ConfidenceLabel = decision.ConfidenceTierLabel,
                    HorizonLabel    = FormatHorizonIndex(preset.HorizonIndex),
                });
            }

            HasComparison = true;
        }
        catch (OperationCanceledException) { }
        catch { /* non-critical — comparison errors are swallowed */ }
        finally
        {
            IsComparing = false;
        }
    }

    [RelayCommand]
    private void ClearResult()
    {
        _activeCts?.Cancel();
        HasResult         = false;
        HasDecision       = false;
        InsufficientData  = false;
        ScenarioNarrative = "";
        HeadlineVerdict   = "";
        DecisionLabel     = "";
        DivergenceSummary = "";
        ConfidenceFactors.Clear();
        TrajectoryPoints.Clear();
        WithActionPoints.Clear();
        WithoutActionPoints.Clear();
        RiskFactors.Clear();
        Mitigations.Clear();
        HistoricalBasis.Clear();
    }

    // ── Result application ────────────────────────────────────────────────────

    private void ApplyResult(SimulationScenario result)
    {
        InsufficientData  = !result.HasSufficientData;
        ScenarioNarrative = result.NarrativeSummary;
        SimulatedAt       = $"Simulated at {result.SimulatedAt:HH:mm:ss}";

        // Confidence
        var conf = result.Confidence;
        ConfidenceValue      = conf.OverallPercent;
        ConfidencePercent    = $"{conf.OverallPercent}%";
        ConfidenceLabel      = ConfidenceTier(conf.OverallPercent);
        ConfidenceColor      = ConfidenceColor_(conf.OverallPercent);
        ConfidenceExplanation = conf.ExplanationText;

        ConfidenceFactors.Clear();
        foreach (var f in conf.Factors) ConfidenceFactors.Add(f);

        // Risk
        var risk = result.Risk;
        RiskLevel          = risk.Label;
        RiskColor          = RiskColor_(risk.Level);
        RiskGlyph          = RiskGlyph_(risk.Level);
        IsReversible       = risk.IsReversible;
        RecurrencePercent  = $"~{risk.RecurrenceLikelihoodPercent}%";
        RollbackPercent    = $"~{risk.RollbackLikelihoodPercent}%";

        RiskFactors.Clear();
        foreach (var f in risk.RiskFactors)
            RiskFactors.Add(new RiskFactorViewModel { Text = f, IsWarning = true });

        Mitigations.Clear();
        foreach (var m in risk.Mitigations) Mitigations.Add(m);

        // Historical basis
        HistoricalBasis.Clear();
        foreach (var b in result.HistoricalBasis)
            HistoricalBasis.Add(new HistoricalBasisEntryViewModel
            {
                DataSource   = b.DataSource,
                Description  = b.Description,
                Contribution = $"{b.ContributionPercent}% weight",
            });

        // Branches
        if (result.WithActionBranch is { } withAction)
            ApplyBranch(withAction, WithAction, "#60A5FA");
        if (result.WithoutActionBranch is { } withoutAction)
            ApplyBranch(withoutAction, WithoutAction, "#6B7280");

        // Trajectory visualization — bar chart + canvas polyline chart
        BuildTrajectory(result);
        BuildChartPoints(result);

        // Decision intelligence — derive the headline verdict and decision card
        var decision         = SimulationDecisionEngine.Derive(result);
        HeadlineVerdict      = decision.HeadlineVerdict;
        DecisionLabel        = decision.DecisionLabel;
        DecisionColor        = decision.DecisionColor;
        UrgencyLabel         = decision.UrgencyLabel;
        UrgencyColor         = decision.UrgencyColor;
        ProjectedGainSummary = decision.ProjectedGainSummary;
        DivergenceSummary    = decision.DivergenceSummary;
        ConfidenceTierLabel  = decision.ConfidenceTierLabel;
        ConfidenceTierColor  = decision.ConfidenceTierColor;
        HasDecision          = true;

        HasResult = true;

        // Refresh intelligence context now that HasResult and ConfidenceValue are updated.
        // This recomputes the confidence band dimensions against the new simulation result.
        RefreshIntelligenceContext();

        // Auto-save snapshot to persisted history (fire-and-forget, best-effort)
        _ = AutoSaveSnapshotAsync(result, decision);
    }

    private static void ApplyBranch(
        SimulationBranch       branch,
        BranchOutcomeViewModel vm,
        string                 accentColor)
    {
        var s = branch.ProjectedState;
        vm.Label          = branch.Label;
        vm.Description    = branch.Description;
        vm.Narrative      = branch.NarrativeSummary;
        vm.AccentColor    = accentColor;
        vm.StabilityPercent = $"~{s.StabilityProbabilityPercent:F0}%";

        vm.HasReliefDays    = s.EstimatedReliefDays > 0;
        vm.ReliefDays       = s.EstimatedReliefDays > 0
                              ? $"~{s.EstimatedReliefDays:F0} days"
                              : "";

        vm.HasStartupImpact  = s.StartupSettlingSeconds > 30;
        vm.StartupImpact     = s.StartupSettlingSeconds > 30
                               ? $"~{s.StartupSettlingSeconds / 60:F0} min settling"
                               : "";

        vm.Metrics.Clear();
        vm.Metrics.Add(MetricRow("CPU",  s.CpuDelta,  $"{s.Cpu:F0}%"));
        vm.Metrics.Add(MetricRow("RAM",  s.RamDelta,  $"{s.Ram:F0}%"));
        vm.Metrics.Add(MetricRow("Disk", s.DiskDelta, $"{s.Disk:F0}%"));

        vm.KeyInsights.Clear();
        foreach (var ki in branch.KeyInsights) vm.KeyInsights.Add(ki);
    }

    private static ProjectedMetricViewModel MetricRow(
        string label, double delta, string projected)
    {
        bool hasChange = Math.Abs(delta) > 0.5;
        string deltaStr = hasChange
            ? $"{(delta > 0 ? "+" : "")}{delta:F1}%"
            : "Stable";
        string color = delta < -0.5 ? "#34D399" : delta > 0.5 ? "#EF4444" : "#6B7280";

        return new ProjectedMetricViewModel
        {
            Label          = label,
            ProjectedValue = projected,
            Delta          = deltaStr,
            DeltaColor     = color,
            HasDelta       = hasChange,
        };
    }

    private void BuildTrajectory(SimulationScenario result)
    {
        TrajectoryPoints.Clear();

        var withBranch    = result.WithActionBranch;
        var withoutBranch = result.WithoutActionBranch;

        if (withBranch is null || withoutBranch is null) return;

        // Sample at most 8 common time points
        int count = Math.Min(withBranch.Trajectory.Count, withoutBranch.Trajectory.Count);
        int step  = Math.Max(1, count / 8);

        for (int i = 0; i < count; i += step)
        {
            var with    = withBranch.Trajectory[i];
            var without = withoutBranch.Trajectory[i];

            string timeLabel = FormatOffset(with.Offset);

            // Use RAM as the primary trajectory metric (most change-sensitive)
            TrajectoryPoints.Add(new TrajectoryPointViewModel
            {
                TimeLabel          = timeLabel,
                WithActionValue    = with.Ram,
                WithoutActionValue = without.Ram,
                WithActionColor    = "#60A5FA",
                WithoutActionColor = "#EF4444",
            });
        }
    }

    // ── Chart point builder ───────────────────────────────────────────────────

    /// <summary>
    /// Populates <see cref="WithActionPoints"/> and <see cref="WithoutActionPoints"/>
    /// from the simulation trajectory for native Canvas/Polyline rendering.
    ///
    /// Chart space: 560 × 130 px.  RAM is the primary metric (0–100%).
    /// Y is inverted — 0% RAM maps to the bottom (y=130), 100% to top (y=0).
    /// Mutates the existing PointCollection instances so the bound Polyline
    /// auto-updates via the VectorChanged notification.
    /// </summary>
    private void BuildChartPoints(SimulationScenario result)
    {
        const double W = 560.0;
        const double H = 130.0;

        WithActionPoints.Clear();
        WithoutActionPoints.Clear();

        var withBranch    = result.WithActionBranch;
        var withoutBranch = result.WithoutActionBranch;
        if (withBranch is null || withoutBranch is null) return;

        int count = Math.Min(withBranch.Trajectory.Count, withoutBranch.Trajectory.Count);
        if (count == 0) return;

        for (int i = 0; i < count; i++)
        {
            double x        = count == 1 ? 0.0 : W * i / (count - 1);
            double yWith    = H - Math.Clamp(withBranch.Trajectory[i].Ram,    0, 100) / 100.0 * H;
            double yWithout = H - Math.Clamp(withoutBranch.Trajectory[i].Ram, 0, 100) / 100.0 * H;

            WithActionPoints.Add(new Point(x, yWith));
            WithoutActionPoints.Add(new Point(x, yWithout));
        }
    }

    // ── Simulation history ────────────────────────────────────────────────────

    /// <summary>
    /// Captures a snapshot of the completed simulation and persists it to disk.
    /// Called fire-and-forget from ApplyResult — never blocks the UI.
    /// </summary>
    private async Task AutoSaveSnapshotAsync(SimulationScenario result, SimulationDecision decision)
    {
        try
        {
            // Capture telemetry BEFORE any awaits — this is the "before" reading for
            // outcome verification (measuring whether the prediction came true later).
            var lastReading = AppServices.Telemetry.LastReading;
            double ramBefore = lastReading?.RamPercent ?? 50;
            double cpuBefore = lastReading?.CpuPercent ?? 50;

            var scenarioLabel = ScenarioChips
                .FirstOrDefault(c => c.ScenarioType == result.ScenarioType)?.Label
                ?? result.ScenarioType.ToString();

            var with = result.WithActionBranch?.ProjectedState;

            var snapshot = new SimulationSnapshot(
                Id:                  Guid.NewGuid(),
                CreatedAt:           result.SimulatedAt,
                ScenarioLabel:       scenarioLabel,
                HeadlineVerdict:     decision.HeadlineVerdict,
                DecisionLabel:       decision.DecisionLabel,
                DecisionColor:       decision.DecisionColor,
                ConfidenceTierLabel: decision.ConfidenceTierLabel,
                ConfidenceTierColor: decision.ConfidenceTierColor,
                RamDelta:            with?.RamDelta  ?? 0,
                CpuDelta:            with?.CpuDelta  ?? 0,
                DiskDelta:           with?.DiskDelta ?? 0,
                ConfidencePercent:   result.Confidence.OverallPercent,
                HorizonLabel:        HorizonLabel);

            await _historyService.SaveAsync(snapshot).ConfigureAwait(true);

            // Register this simulation for outcome verification.
            // After 30 minutes, TryEvaluatePending() will compare actual vs predicted metrics.
            _outcomeVerification.Register(
                snapshotId:        snapshot.Id,
                scenarioType:      result.ScenarioType.ToString(),
                ramBefore:         ramBefore,
                cpuBefore:         cpuBefore,
                predictedRamDelta: with?.RamDelta  ?? 0,
                predictedCpuDelta: with?.CpuDelta  ?? 0,
                measurementWindow: TimeSpan.FromMinutes(30));

            RefreshHistory();
        }
        catch
        {
            // Best-effort — history failure never disrupts the simulation view
        }
    }

    /// <summary>
    /// Rebuilds <see cref="SnapshotHistory"/> from the persisted history list.
    /// Called on VM construction (to restore previous sessions) and after each save.
    /// Enriches each entry with outcome accuracy if a verification result exists.
    /// </summary>
    private void RefreshHistory()
    {
        SnapshotHistory.Clear();

        foreach (var snap in _historyService.GetAll())
        {
            var outcome  = _outcomeVerification.GetOutcome(snap.Id);
            bool hasAcc  = outcome is not null;
            string accLabel = hasAcc
                ? $"{outcome!.PredictionAccuracy:F0}% accurate"
                : "";
            string accColor = hasAcc ? AccuracyColor(outcome!.PredictionAccuracy) : "#6B7280";

            SnapshotHistory.Add(new SimulationSnapshotViewModel
            {
                ScenarioLabel       = snap.ScenarioLabel,
                HeadlineVerdict     = snap.HeadlineVerdict,
                DecisionLabel       = snap.DecisionLabel,
                DecisionColor       = snap.DecisionColor,
                ConfidenceTierLabel = snap.ConfidenceTierLabel,
                ConfidenceTierColor = snap.ConfidenceTierColor,
                TimeAgo             = FormatTimeAgo(snap.CreatedAt),
                RamDeltaLabel       = FormatMetricDelta(snap.RamDelta),
                RamDeltaColor       = MetricDeltaColor(snap.RamDelta),
                CpuDeltaLabel       = FormatMetricDelta(snap.CpuDelta),
                CpuDeltaColor       = MetricDeltaColor(snap.CpuDelta),
                AccuracyLabel       = accLabel,
                AccuracyColor       = accColor,
                HasAccuracy         = hasAcc,
            });
        }

        HasHistory = SnapshotHistory.Count > 0;
    }

    /// <summary>
    /// Convenience wrapper — evaluates pending outcome verifications using the
    /// most recent telemetry reading.  Safe to call when no reading exists yet.
    /// </summary>
    private void EvaluatePending()
    {
        var reading = AppServices.Telemetry.LastReading;
        if (reading is not null)
            _outcomeVerification.TryEvaluatePending(reading.RamPercent, reading.CpuPercent);
    }

    // ── Scenario chip builder ─────────────────────────────────────────────────

    private void BuildScenarioChips()
    {
        var scenarios = new[]
        {
            (SimulationScenarioType.RestartSystem,          "Restart",         "What if I restart?",                    ""),
            (SimulationScenarioType.DisableStartupApps,     "Startup Apps",    "What if I disable startup apps?",        ""),
            (SimulationScenarioType.CleanupDisk,            "Cleanup",         "What if I run cleanup?",                ""),
            (SimulationScenarioType.RepairWindows,          "Repair",          "What if I run Windows repair?",         ""),
            (SimulationScenarioType.TerminateProcess,       "Terminate",       "What if I terminate a heavy process?",  ""),
            (SimulationScenarioType.DeferMaintenance,       "Defer",           "What if I ignore this for a week?",     ""),
            (SimulationScenarioType.ContinueCurrentWorkload, "Continue",       "What if I continue this workload?",     ""),
            (SimulationScenarioType.StorageGrowthContinuation, "Storage Growth", "What if disk keeps filling?",         ""),
        };

        foreach (var (type, label, description, glyph) in scenarios)
        {
            ScenarioChips.Add(new ScenarioTypeChipViewModel
            {
                ScenarioType = type,
                Label        = label,
                Description  = description,
                Glyph        = glyph,
                IsSelected   = type == _selectedScenario,
            });
        }
    }

    private void BuildScenarioPresets()
    {
        foreach (var preset in SimulationPresetLibrary.All)
        {
            ScenarioPresets.Add(new ScenarioPresetViewModel
            {
                Name         = preset.Name,
                Description  = preset.Description,
                Glyph        = preset.Glyph,
                ScenarioType = preset.ScenarioType,
                HorizonIndex = preset.HorizonIndex,
            });
        }
    }

    // ── Display helpers ───────────────────────────────────────────────────────

    private static SimulationHorizon IndexToHorizon(int index) => index switch
    {
        0 => SimulationHorizon.FifteenMinutes,
        1 => SimulationHorizon.OneHour,
        2 => SimulationHorizon.FourHours,
        3 => SimulationHorizon.TwentyFourHours,
        4 => SimulationHorizon.SevenDays,
        _ => SimulationHorizon.FourHours,
    };

    private static string ConfidenceTier(int pct) => pct switch
    {
        >= 75 => "Good confidence",
        >= 55 => "Moderate confidence",
        >= 40 => "Limited confidence",
        _     => "Preliminary estimate",
    };

    private static string ConfidenceColor_(int pct) => pct switch
    {
        >= 75 => "#34D399",
        >= 55 => "#F59E0B",
        _     => "#EF4444",
    };

    private static string RiskColor_(ProjectedRiskLevel level) => level switch
    {
        ProjectedRiskLevel.Low      => "#34D399",
        ProjectedRiskLevel.Moderate => "#F59E0B",
        ProjectedRiskLevel.Elevated => "#EF4444",
        ProjectedRiskLevel.High     => "#EF4444",
        _                           => "#6B7280",
    };

    private static string RiskGlyph_(ProjectedRiskLevel level) => level switch
    {
        ProjectedRiskLevel.Low      => "",
        ProjectedRiskLevel.Moderate => "",
        ProjectedRiskLevel.Elevated => "",
        ProjectedRiskLevel.High     => "",
        _                           => "",
    };

    private static string FormatOffset(TimeSpan offset)
    {
        if (offset.TotalMinutes < 1)   return "now";
        if (offset.TotalHours   < 1)   return $"{(int)offset.TotalMinutes}m";
        if (offset.TotalDays    < 1)   return $"{(int)offset.TotalHours}h";
        return $"{(int)offset.TotalDays}d";
    }

    /// <summary>Formats a metric delta as "+N%" / "-N%" / "Stable".</summary>
    private static string FormatMetricDelta(double delta)
    {
        if (Math.Abs(delta) < 0.5) return "Stable";
        return $"{(delta > 0 ? "+" : "")}{delta:F0}%";
    }

    /// <summary>Green for improvement (negative delta), red for degradation, gray for stable.</summary>
    private static string MetricDeltaColor(double delta) =>
        delta < -0.5 ? "#34D399"
        : delta > 0.5 ? "#EF4444"
        : "#6B7280";

    /// <summary>Returns "Just now" / "Xm ago" / "Xh ago" / "Xd ago".</summary>
    private static string FormatTimeAgo(DateTimeOffset createdAt)
    {
        var elapsed = DateTimeOffset.Now - createdAt;
        if (elapsed.TotalMinutes < 1) return "Just now";
        if (elapsed.TotalHours   < 1) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalDays    < 1) return $"{(int)elapsed.TotalHours}h ago";
        return $"{(int)elapsed.TotalDays}d ago";
    }

    /// <summary>Color for a prediction accuracy score (green ≥ 70, amber ≥ 45, red below).</summary>
    private static string AccuracyColor(double accuracy) => accuracy switch
    {
        >= 70 => "#34D399",
        >= 45 => "#F59E0B",
        _     => "#EF4444",
    };

    /// <summary>Short label for a horizon index (for comparison table display).</summary>
    private static string FormatHorizonIndex(int index) => index switch
    {
        0 => "15 min",
        1 => "1 hr",
        2 => "4 hrs",
        3 => "24 hrs",
        4 => "7 days",
        _ => "4 hrs",
    };

    // ── Anomaly intelligence context ──────────────────────────────────────────

    private void OnAnomaliesUpdated(object? sender, IReadOnlyList<DetectedAnomaly> _)
        => RefreshIntelligenceContext();

    private void OnDriftsUpdated(object? sender, IReadOnlyList<DetectedDrift> _)
        => RefreshIntelligenceContext();

    private void OnWarningsUpdated(object? sender, IReadOnlyList<EarlyWarning> _)
        => RefreshIntelligenceContext();

    /// <summary>
    /// Rebuilds all chart-overlay and context-panel data from the current
    /// anomaly / drift / warning state.  Called:
    ///   • on construction (initial seed)
    ///   • whenever any upstream intelligence service fires an update event
    ///   • at the end of ApplyResult() so the confidence band reflects the
    ///     new simulation's confidence score
    /// </summary>
    private void RefreshIntelligenceContext()
    {
        var anomalies = AppServices.AnomalyDetection.CurrentAnomalies;
        var drifts    = AppServices.DriftDetection.CurrentDrifts;
        var warnings  = AppServices.EarlyWarning.CurrentWarnings;

        HasAnomalyContext = anomalies.Count > 0;
        HasDriftContext   = drifts.Count > 0;
        HasWarningContext = warnings.Count > 0;

        // ── Anomaly dot Y — position at live RAM level on the chart ───────────
        // Canvas height is 130 px; RAM 0% → Y=130 (bottom), 100% → Y=0 (top).
        // Subtract 5 to centre the 10 px dot on the reading.
        double ramNow = AppServices.Telemetry.LastReading?.RamPercent ?? 50.0;
        AnomalyDotY   = 130.0 - Math.Clamp(ramNow, 0.0, 100.0) / 100.0 * 130.0 - 5.0;

        // ── Confidence band — uncertainty envelope around the mid-chart line ──
        // At 100% confidence the band collapses to 0 height (perfect certainty).
        // At 0% confidence the band spans ±32.5 px (full uncertainty).
        if (HasResult && ConfidenceValue > 0)
        {
            double margin      = (100.0 - ConfidenceValue) / 100.0 * 32.5;
            ConfidenceBandTop    = Math.Max(0.0,   65.0 - margin);
            ConfidenceBandHeight = Math.Min(130.0, margin * 2.0);
        }

        // ── Build ordered context items: anomalies → drifts → warnings ────────
        var items = new List<IntelContextItem>(anomalies.Count + drifts.Count + Math.Min(warnings.Count, 2));

        foreach (var a in anomalies)
            items.Add(new IntelContextItem(
                Glyph:       a.MetricGlyph,
                Color:       a.SeverityColor,
                BadgeColor:  a.SeverityBadgeColor,
                Label:       $"{a.Metric} anomaly  ·  {a.SeverityLabel}",
                Description: a.Description));

        foreach (var d in drifts)
            items.Add(new IntelContextItem(
                Glyph:       d.MetricGlyph,
                Color:       d.DirectionColor,
                BadgeColor:  "#1F2937",
                Label:       $"{d.MetricName} drift  ·  {d.SeverityLabel}",
                Description: d.Summary));

        // Cap early-warning items at 2 — the panel is supplementary, not exhaustive.
        foreach (var w in warnings.Take(2))
            items.Add(new IntelContextItem(
                Glyph:       w.Glyph,
                Color:       w.SeverityColor,
                BadgeColor:  w.SeverityBadgeColor,
                Label:       w.Title,
                Description: w.Explanation));

        IntelligenceContextItems = items;

        int total = anomalies.Count + drifts.Count + warnings.Count;
        IntelligenceContextLabel = total == 0
            ? ""
            : total == 1
                ? "1 active intelligence signal may affect this projection"
                : $"{total} active intelligence signals may affect this projection";

        // Notify all dependent properties in one pass.
        OnPropertyChanged(nameof(HasAnomalyContext));
        OnPropertyChanged(nameof(HasDriftContext));
        OnPropertyChanged(nameof(HasWarningContext));
        OnPropertyChanged(nameof(HasIntelligenceContext));
        OnPropertyChanged(nameof(AnomalyDotVisibility));
        OnPropertyChanged(nameof(DriftOverlayVisibility));
        OnPropertyChanged(nameof(WarningFlagVisibility));
        OnPropertyChanged(nameof(IntelligenceContextVisibility));
        OnPropertyChanged(nameof(ConfidenceBandVisibility));
        OnPropertyChanged(nameof(AnomalyDotY));
        OnPropertyChanged(nameof(ConfidenceBandTop));
        OnPropertyChanged(nameof(ConfidenceBandHeight));
        OnPropertyChanged(nameof(IntelligenceContextItems));
        OnPropertyChanged(nameof(IntelligenceContextLabel));
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Unsubscribes from app-level intelligence services and cancels any
    /// in-progress simulation.  Called by <see cref="SimulationPage.OnNavigatedFrom"/>.
    /// </summary>
    public void Cleanup()
    {
        AppServices.AnomalyDetection.AnomaliesUpdated -= OnAnomaliesUpdated;
        AppServices.DriftDetection.DriftsUpdated       -= OnDriftsUpdated;
        AppServices.EarlyWarning.WarningsUpdated        -= OnWarningsUpdated;
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _activeCts = null;
    }
}
