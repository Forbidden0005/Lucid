using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Intelligence;
using Lucid.Services.Intelligence.Arbitration;
using Lucid.Services.Intelligence.Baselines;
using Lucid.Services.Intelligence.Calibration;
using Lucid.Services.Intelligence.Patterns;
using Lucid.Services.Interaction.Attention;
using Lucid.Services.Interaction.Recommendations;
using Lucid.Services.Reasoning.Cognitive;
using Lucid.Services.Reasoning.Context;
using Lucid.Services.Reasoning.Memory;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Lucid.ViewModels;

// ── Sub-ViewModels ────────────────────────────────────────────────────────────

/// <summary>ViewModel for a single operational inference card.</summary>
public sealed class InferenceCardViewModel
{
    public string Headline              { get; init; } = string.Empty;
    public string Explanation           { get; init; } = string.Empty;
    public string Trajectory            { get; init; } = string.Empty;
    public string PriorityLabel         { get; init; } = string.Empty;
    public string PriorityColor         { get; init; } = "#6B7280";
    public string ConfidenceText        { get; init; } = string.Empty;
    public string DomainsText           { get; init; } = string.Empty;
    public string EvidenceCountText     { get; init; } = string.Empty;
    public string WorkloadContextText   { get; init; } = string.Empty;
    public bool   IsSuppressed          { get; init; }
    public string SuppressionReason     { get; init; } = string.Empty;

    public Visibility TrajectoryVisibility    => !string.IsNullOrEmpty(Trajectory)   ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SuppressedVisibility    => IsSuppressed                         ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NotSuppressedVisibility => !IsSuppressed                        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WorkloadVisibility      => !string.IsNullOrEmpty(WorkloadContextText) ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>ViewModel for a detected recurring operational pattern row.</summary>
public sealed class PatternRowViewModel
{
    public string Description           { get; init; } = string.Empty;
    public string OccurrenceText        { get; init; } = string.Empty;
    public string ConfidenceText        { get; init; } = string.Empty;
    public string ConfidenceColor       { get; init; } = "#6B7280";
    public string DomainsText           { get; init; } = string.Empty;
    public string WorkloadContextText   { get; init; } = string.Empty;
    public string RecurrenceSummary     { get; init; } = string.Empty;
    public bool   IsStable              { get; init; }
    public bool   IsWellEstablished     { get; init; }
    public string StabilityLabel        { get; init; } = string.Empty;
    public string StabilityColor        { get; init; } = "#6B7280";

    public Visibility WorkloadVisibility => !string.IsNullOrEmpty(WorkloadContextText) ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>ViewModel for a single adaptive baseline profile row.</summary>
public sealed class BaselineProfileViewModel
{
    public string MetricName    { get; init; } = string.Empty;
    public string WorkloadLabel { get; init; } = string.Empty;
    public string RangeText     { get; init; } = string.Empty;
    public string SamplesText   { get; init; } = string.Empty;
    public bool   IsReliable    { get; init; }
    public string ReliabilityLabel  { get; init; } = string.Empty;
    public string ReliabilityColor  { get; init; } = "#6B7280";
}

/// <summary>ViewModel for a unified recommendation card.</summary>
public sealed class RecommendationCardViewModel
{
    public string Headline              { get; init; } = string.Empty;
    public string Summary               { get; init; } = string.Empty;
    public string UrgencyPhrase         { get; init; } = string.Empty;
    public string SeverityLabel         { get; init; } = string.Empty;
    public string SeverityColor         { get; init; } = "#6B7280";
    public string Domain                { get; init; } = string.Empty;
    public string ActionCountText       { get; init; } = string.Empty;
    public string ReasoningExplanation  { get; init; } = string.Empty;
    public string FatigueNote           { get; init; } = string.Empty;
    public bool   HasFatigueNote        { get; init; }

    public Visibility FatigueNoteVisibility      => HasFatigueNote                         ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReasoningVisibility        => !string.IsNullOrEmpty(ReasoningExplanation) ? Visibility.Visible : Visibility.Collapsed;
}

// ── Main ViewModel ────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for the Reasoning page.
///
/// Surfaces the output of the cognitive intelligence layers (Phases 19–21):
///   • Workload context (OperationalContextSynthesizer)
///   • Active inferences (CognitiveReasoningEngine)
///   • Recommendations (UnifiedRecommendationService)
///   • Recurring patterns (PatternIntelligenceEngine)
///   • Adaptive baselines (AdaptiveBaselineTracker)
///   • Calibration health (ConfidenceCalibrationEngine)
///   • Attention state (AttentionCoordinator)
///
/// Threading: all UI updates marshal via DispatcherQueue captured in constructor.
/// </summary>
public sealed partial class ReasoningPageViewModel : ObservableObject
{
    private readonly IOperationalContextSynthesizer _contextSynthesizer;
    private readonly ICognitiveReasoningEngine      _reasoning;
    private readonly PatternIntelligenceEngine      _patterns;
    private readonly AdaptiveBaselineTracker        _baselines;
    private readonly ConfidenceCalibrationEngine    _calibration;
    private readonly UnifiedRecommendationService   _recommendations;
    private readonly AttentionCoordinator           _attention;
    private readonly DispatcherQueue                _ui;
    private readonly IRecommendationArbitrator      _arbitrator;
    private readonly ISystemInsightEngine           _intelligence;

    // ── Loading state ─────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingVisibility))]
    [NotifyPropertyChangedFor(nameof(ContentVisibility))]
    private bool _isLoading = true;

    // ── Context section ───────────────────────────────────────────────────────

    [ObservableProperty] private string _workloadLabel          = "Analyzing…";
    [ObservableProperty] private string _workloadConfidenceText = "";
    [ObservableProperty] private string _workloadActivityGlyph  = "";
    [ObservableProperty] private string _expectedCpuRangeText   = "";
    [ObservableProperty] private string _expectedRamRangeText   = "";
    [ObservableProperty] private string _expectedThermalRangeText = "";
    [ObservableProperty] private bool   _highCpuExpected;
    [ObservableProperty] private bool   _postBootTransient;
    [ObservableProperty] private bool   _windowsUpdateActive;
    [ObservableProperty] private string _contextFlagsText       = "";
    [ObservableProperty] private string _lastRefreshedText      = "Not yet refreshed";

    // ── Calibration section ───────────────────────────────────────────────────

    [ObservableProperty] private string _calibrationStatusText  = "No data yet";
    [ObservableProperty] private string _calibrationStatusColor = "#6B7280";
    [ObservableProperty] private bool   _hasDrift;
    [ObservableProperty] private string _driftMessage           = "";
    [ObservableProperty] private string _adjustmentFactorText   = "";
    [ObservableProperty] private int    _trackedInferenceTypes;

    // ── Attention section ─────────────────────────────────────────────────────

    [ObservableProperty] private int    _remainingAttentionBudget;
    [ObservableProperty] private string _attentionBudgetText    = "";
    [ObservableProperty] private string _attentionBudgetColor   = "#6B7280";

    // ── Summary counts ────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoInferencesVisibility))]
    [NotifyPropertyChangedFor(nameof(InferenceListVisibility))]
    private bool _hasInferences;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoRecommendationsVisibility))]
    [NotifyPropertyChangedFor(nameof(RecommendationListVisibility))]
    private bool _hasRecommendations;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoPatternsVisibility))]
    [NotifyPropertyChangedFor(nameof(PatternListVisibility))]
    private bool _hasPatterns;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoBaselinesVisibility))]
    [NotifyPropertyChangedFor(nameof(BaselineListVisibility))]
    private bool _hasBaselines;

    // ── Collections ───────────────────────────────────────────────────────────

    public ObservableCollection<InferenceCardViewModel>      Inferences      { get; } = [];
    public ObservableCollection<RecommendationCardViewModel> Recommendations { get; } = [];
    public ObservableCollection<PatternRowViewModel>         Patterns        { get; } = [];
    public ObservableCollection<BaselineProfileViewModel>    Baselines       { get; } = [];

    // ── Visibility helpers ────────────────────────────────────────────────────

    public Visibility LoadingVisibility            => IsLoading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ContentVisibility            => !IsLoading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoInferencesVisibility       => !HasInferences ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InferenceListVisibility      => HasInferences  ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoRecommendationsVisibility  => !HasRecommendations ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RecommendationListVisibility => HasRecommendations  ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoPatternsVisibility         => !HasPatterns ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PatternListVisibility        => HasPatterns  ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoBaselinesVisibility        => !HasBaselines ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BaselineListVisibility       => HasBaselines  ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DriftVisibility              => HasDrift      ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ContextFlagsVisibility       => !string.IsNullOrEmpty(ContextFlagsText) ? Visibility.Visible : Visibility.Collapsed;

    // ── Constructor ───────────────────────────────────────────────────────────

    public ReasoningPageViewModel(
        IOperationalContextSynthesizer  contextSynthesizer,
        ICognitiveReasoningEngine       reasoning,
        PatternIntelligenceEngine       patterns,
        AdaptiveBaselineTracker         baselines,
        ConfidenceCalibrationEngine     calibration,
        UnifiedRecommendationService    recommendations,
        AttentionCoordinator            attention,
        DispatcherQueue                 ui,
        IRecommendationArbitrator       arbitrator,
        ISystemInsightEngine            intelligence)
    {
        _contextSynthesizer = contextSynthesizer;
        _reasoning          = reasoning;
        _patterns           = patterns;
        _baselines          = baselines;
        _calibration        = calibration;
        _recommendations    = recommendations;
        _arbitrator         = arbitrator;
        _intelligence       = intelligence;
        _attention          = attention;
        _ui                 = ui;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            await RefreshCoreAsync(CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            await RefreshCoreAsync(CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RefreshCoreAsync(CancellationToken ct)
    {
        // Run all service calls concurrently on the thread pool.
        var contextTask    = Task.Run(() => _contextSynthesizer.SynthesizeAsync(ct), ct);
        var inferenceTask  = Task.Run(() => _reasoning.AnalyzeAsync(ct), ct);

        await Task.WhenAll(contextTask, inferenceTask).ConfigureAwait(true);

        var context    = contextTask.Result;
        var inferences = inferenceTask.Result;

        // Pattern analysis from the current inference cycle.
        // FromInference() gives us proper InferenceType for grouping.
        var historyRecords = inferences
            .Select(HistoricalInferenceRecord.FromInference)
            .ToList()
            .AsReadOnly();
        var patterns = _patterns.Analyze(historyRecords);

        // Baselines and calibration are synchronous reads.
        var baselineProfiles = _baselines.GetAllEstablishedProfiles();
        var calibration      = _calibration.Refresh();

        // Recommendations require arbitration (synchronous).
        var clusters = _arbitrator.Arbitrate(
            _intelligence.CurrentInsights, context, inferences);
        var unified  = _recommendations.GetRecommendations(clusters);

        // Marshal all UI updates to the dispatcher thread.
        _ui.TryEnqueue(() =>
        {
            ApplyContext(context);
            ApplyInferences(inferences);
            ApplyRecommendations(unified);
            ApplyPatterns(patterns);
            ApplyBaselines(baselineProfiles);
            ApplyCalibration(calibration);
            ApplyAttention();
            LastRefreshedText = $"Updated {DateTime.Now:h:mm tt}";
        });
    }

    // ── Apply helpers ─────────────────────────────────────────────────────────

    private void ApplyContext(ContextProfile ctx)
    {
        WorkloadLabel           = ctx.WorkloadLabel;
        WorkloadConfidenceText  = $"{ctx.ClassificationConfidence:P0} confidence";
        WorkloadActivityGlyph   = ActivityClassToGlyph(ctx.PrimaryActivity);
        ExpectedCpuRangeText    = $"{ctx.ExpectedCpuRange.Low:F0}–{ctx.ExpectedCpuRange.High:F0}%";
        ExpectedRamRangeText    = $"{ctx.ExpectedRamRange.Low:F0}–{ctx.ExpectedRamRange.High:F0}%";
        ExpectedThermalRangeText = $"{ctx.ExpectedThermalRange.Low:F0}–{ctx.ExpectedThermalRange.High:F0}°C";
        HighCpuExpected         = ctx.HighCpuIsExpected;
        PostBootTransient       = ctx.IsPostBootTransient;
        WindowsUpdateActive     = ctx.WindowsUpdateActive;

        var flags = new List<string>();
        if (ctx.IsPostBootTransient) flags.Add("Post-boot transient");
        if (ctx.WindowsUpdateActive)  flags.Add("Windows Update active");
        if (ctx.HighCpuIsExpected)    flags.Add("High CPU expected");
        ContextFlagsText = string.Join("  ·  ", flags);
    }

    private void ApplyInferences(IReadOnlyList<OperationalInference> inferences)
    {
        Inferences.Clear();
        foreach (var inf in inferences.OrderByDescending(i => i.Priority))
        {
            Inferences.Add(new InferenceCardViewModel
            {
                Headline            = inf.Headline,
                Explanation         = inf.Explanation,
                Trajectory          = inf.Trajectory ?? string.Empty,
                PriorityLabel       = inf.Priority.ToLabel(),
                PriorityColor       = inf.Priority.ToColor(),
                ConfidenceText      = $"{inf.Confidence.Label}  ·  {inf.Confidence.Percentage}",
                DomainsText         = string.Join(", ", inf.Domains),
                EvidenceCountText   = $"{inf.Evidence.Count} signal{(inf.Evidence.Count != 1 ? "s" : "")}",
                WorkloadContextText = inf.WorkloadContextAtInference ?? string.Empty,
                IsSuppressed        = inf.IsSuppressed,
                SuppressionReason   = inf.SuppressionReason ?? string.Empty,
            });
        }
        HasInferences = Inferences.Count > 0;
    }

    private void ApplyRecommendations(IReadOnlyList<UnifiedRecommendation> unified)
    {
        Recommendations.Clear();
        foreach (var rec in unified.Take(6))
        {
            Recommendations.Add(new RecommendationCardViewModel
            {
                Headline             = rec.Headline,
                Summary              = rec.Summary,
                UrgencyPhrase        = rec.UrgencyPhrase,
                SeverityLabel        = rec.SeverityLabel,
                SeverityColor        = SeverityToColor(rec.Severity),
                Domain               = rec.Domain,
                ActionCountText      = rec.ActionCount == 1
                    ? "1 action available"
                    : $"{rec.ActionCount} actions available",
                ReasoningExplanation = rec.ReasoningExplanation,
                FatigueNote          = rec.FatigueNote ?? string.Empty,
                HasFatigueNote       = !string.IsNullOrEmpty(rec.FatigueNote),
            });
        }
        HasRecommendations = Recommendations.Count > 0;
    }

    private void ApplyPatterns(IReadOnlyList<OperationalPattern> patterns)
    {
        Patterns.Clear();
        foreach (var p in patterns.OrderByDescending(x => x.PatternConfidence))
        {
            var color = p.PatternConfidence switch
            {
                >= 0.7 => "#22C55E",
                >= 0.4 => "#FBBF24",
                _      => "#6B7280",
            };
            Patterns.Add(new PatternRowViewModel
            {
                Description         = p.Description,
                OccurrenceText      = $"{p.OccurrenceCount}×",
                ConfidenceText      = $"{p.PatternConfidence:P0}",
                ConfidenceColor     = color,
                DomainsText         = string.Join(", ", p.Domains),
                WorkloadContextText = p.DominantWorkloadContext ?? string.Empty,
                RecurrenceSummary   = p.RecurrenceSummary,
                IsStable            = p.IsStable,
                IsWellEstablished   = p.IsWellEstablished,
                StabilityLabel      = p.IsStable ? "Stable pattern" : "Emerging",
                StabilityColor      = p.IsStable ? "#22C55E" : "#94A3B8",
            });
        }
        HasPatterns = Patterns.Count > 0;
    }

    private void ApplyBaselines(IReadOnlyList<WorkloadBaselineProfile> profiles)
    {
        Baselines.Clear();
        foreach (var b in profiles.OrderBy(p => p.MetricName).ThenBy(p => p.WorkloadLabel))
        {
            bool reliable = b.SampleCount >= 10;
            Baselines.Add(new BaselineProfileViewModel
            {
                MetricName      = FormatMetricName(b.MetricName),
                WorkloadLabel   = b.WorkloadLabel,
                RangeText       = $"{b.Mean:F1} ± {b.StdDev:F1}",
                SamplesText     = $"{b.SampleCount} samples",
                IsReliable      = reliable,
                ReliabilityLabel = reliable ? "Established" : "Establishing",
                ReliabilityColor = reliable ? "#22C55E" : "#FBBF24",
            });
        }
        HasBaselines = Baselines.Count > 0;
    }

    private void ApplyCalibration(CalibrationState state)
    {
        TrackedInferenceTypes  = state.TrackedTypeCount;
        HasDrift               = state.DriftReport.HasMeaningfulDrift;
        DriftMessage           = state.DriftReport.Explanation;
        AdjustmentFactorText   = $"{state.RecommendedAdjustment:F2}×";

        if (!state.DriftReport.HasMeaningfulDrift)
        {
            CalibrationStatusText  = "Well-calibrated";
            CalibrationStatusColor = "#22C55E";
        }
        else
        {
            CalibrationStatusText  = state.DriftReport.Direction switch
            {
                DriftDirection.OverConfident  => "Over-confident",
                DriftDirection.UnderConfident => "Under-confident",
                _                             => "Drift detected",
            };
            CalibrationStatusColor = "#FBBF24";
        }
    }

    private void ApplyAttention()
    {
        RemainingAttentionBudget = _attention.RemainingBudget;
        AttentionBudgetText      = RemainingAttentionBudget switch
        {
            0 => "Interrupt budget exhausted",
            1 => "1 interrupt slot remaining",
            _ => $"{RemainingAttentionBudget} interrupt slots remaining",
        };
        AttentionBudgetColor = RemainingAttentionBudget switch
        {
            0 => "#F87171",
            1 => "#FBBF24",
            _ => "#22C55E",
        };
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    private static string ActivityClassToGlyph(ActivityClass a) => a switch
    {
        ActivityClass.Gaming           => "",
        ActivityClass.Compiling        => "",
        ActivityClass.Rendering        => "",
        ActivityClass.VideoStreaming   => "",
        ActivityClass.BrowsingHeavy    => "",
        ActivityClass.Productivity     => "",
        ActivityClass.SystemUpdate     => "",
        ActivityClass.Backup           => "",
        ActivityClass.Idle             => "",
        _                              => "",
    };

    private static string SeverityToColor(RecommendationSeverity s) => s switch
    {
        RecommendationSeverity.Critical    => "#F87171",
        RecommendationSeverity.Stability   => "#FBBF24",
        RecommendationSeverity.Performance => "#60A5FA",
        RecommendationSeverity.Maintenance => "#94A3B8",
        _                                  => "#6B7280",
    };

    private static string FormatMetricName(string raw) => raw switch
    {
        "CpuPercent"    => "CPU %",
        "RamPercent"    => "RAM %",
        "DiskPercent"   => "Disk %",
        "ThermalCelsius" => "Temp °C",
        _               => raw,
    };
}
