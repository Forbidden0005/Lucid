using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lucid.Services.Explain;
using Lucid.Services.Intelligence;
using Lucid.Services.Learning;
using Lucid.Services.Narrative;
using Lucid.Services.Timeline;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

// Alias to avoid ambiguity with local SystemHealthState string property
using NarrativeHealthState = Lucid.Services.Narrative.SystemHealthState;

namespace Lucid.ViewModels;

// ── Sub-ViewModels ─────────────────────────────────────────────────────────────
// Simple data-holder classes used by ItemsControl DataTemplates.
// Properties intentionally do not implement INotifyPropertyChanged —
// the parent ViewModel clears and repopulates the collections wholesale
// each time the explanation updates, which is more efficient for this use case.

public sealed class CausalFactorViewModel
{
    public string  Title             { get; init; } = "";
    public string  Explanation       { get; init; } = "";
    public string  DomainLabel       { get; init; } = "";
    public string  DomainGlyph       { get; init; } = ""; // default: Insights
    public int     ConfidencePercent { get; init; }
    public string  ConfidenceText    { get; init; } = "";
    public string  AttributionText   { get; init; } = "";
    public bool       HasAttribution           { get; init; }
    public Visibility HasAttributionVisibility { get; init; }
    public Brush      AccentBrush              { get; init; } = new SolidColorBrush(Colors.Gray);
}

public sealed class RecentChangeViewModel
{
    public string         Title       { get; init; } = "";
    public string         Detail      { get; init; } = "";
    public string         TimeText    { get; init; } = "";
    public string         TypeGlyph   { get; init; } = "";
    public Brush          DotBrush    { get; init; } = new SolidColorBrush(Colors.Gray);
    public bool           HasDetail           { get; init; }
    public Visibility     HasDetailVisibility { get; init; }
}

public sealed class ForecastOutlookViewModel
{
    public string  Title             { get; init; } = "";
    public string  Projection        { get; init; } = "";
    public string  MitigationHint    { get; init; } = "";
    public bool       HasMitigation           { get; init; }
    public Visibility HasMitigationVisibility { get; init; }
    public string  SeverityGlyph     { get; init; } = "";
    public Brush   SeverityBrush     { get; init; } = new SolidColorBrush(Colors.Gray);
    public int     ConfidencePercent { get; init; }
    public string  ConfidenceText    { get; init; } = "";
}

public sealed class RecommendationViewModel
{
    public int        Rank              { get; init; }
    public string     ActionTitle       { get; init; } = "";
    public string     WhyItMatters      { get; init; } = "";
    public string     ExpectedBenefit   { get; init; } = "";
    public string     EstimatedTime     { get; init; } = "";
    public int        ConfidencePercent { get; init; }
    public bool       CanRollback       { get; init; }
    public string     RollbackLabel     { get; init; } = "";
    public string     RiskLabel         { get; init; } = "";
    public Brush      RankBrush         { get; init; } = new SolidColorBrush(Colors.Gray);

    // ── Effectiveness badge (from learning service) ───────────────────────────
    /// <summary>Short label for the badge chip. Empty when no warm data.</summary>
    public string     EffectivenessLabel    { get; init; } = "";
    /// <summary>Longer sentence for the badge tooltip.</summary>
    public string     EffectivenessText     { get; init; } = "";
    /// <summary>Visibility of the effectiveness badge.</summary>
    public Visibility EffectivenessVisible  { get; init; } = Visibility.Collapsed;
    /// <summary>StaticResource key for the badge brush.</summary>
    public string     EffectivenessBrushKey { get; init; } = "TextSecondaryBrush";
}

public sealed class EvidenceItemViewModel
{
    public string Label     { get; init; } = "";
    public string Value     { get; init; } = "";
    public string KindLabel { get; init; } = "";
    public Brush  KindBrush { get; init; } = new SolidColorBrush(Colors.Gray);
}

// ── Main ViewModel ─────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for the Explain My PC flagship page.
///
/// Subscribes to <see cref="IExplainMyPcEngine.ExplanationUpdated"/> and unpacks
/// each <see cref="OperationalExplanation"/> into observable collections and
/// properties that the page binds to directly.
///
/// All updates arrive on the UI thread (inherited from the engine's upstream
/// narrative service), so ObservableCollection mutation is safe without dispatch.
/// </summary>
public partial class ExplainViewModel : ObservableObject
{
    private readonly IExplainMyPcEngine _engine;

    // ── Section 1: System Summary ─────────────────────────────────────────────

    [ObservableProperty] private string _headline      = "Analyzing your system…";
    [ObservableProperty] private string _healthState   = "Analyzing";
    [ObservableProperty] private string _summaryNarrative = "Please wait a moment while we evaluate your system.";
    [ObservableProperty] private int    _confidencePercent = 0;
    [ObservableProperty] private string _confidenceText    = "";
    [ObservableProperty] private string _dataPointsText    = "";
    [ObservableProperty] private string _lastUpdatedText   = "";
    [ObservableProperty] private Brush  _healthStateBrush  = new SolidColorBrush(Colors.Gray);
    [ObservableProperty] private Brush  _heroAccentBrush   = new SolidColorBrush(Colors.Gray);
    [ObservableProperty] private string _heroGlyph         = "";
    [ObservableProperty] private Visibility _dominantCausesVisibility = Visibility.Collapsed;

    public ObservableCollection<string> DominantCauses { get; } = [];

    // ── Section 2: What Changed? ──────────────────────────────────────────────

    [ObservableProperty] private string     _recentChangesLabel      = "No significant changes in the last 4 hours";
    [ObservableProperty] private Visibility _recentChangesVisibility = Visibility.Collapsed;
    [ObservableProperty] private Visibility _noRecentChangesVisibility = Visibility.Visible;

    public ObservableCollection<RecentChangeViewModel> RecentChanges { get; } = [];

    // ── Section 3: What's Causing This? ──────────────────────────────────────

    [ObservableProperty] private string     _causalSectionLabel        = "No active issues detected";
    [ObservableProperty] private Visibility _causalFactorsVisibility   = Visibility.Collapsed;
    [ObservableProperty] private Visibility _noCausalFactorsVisibility = Visibility.Visible;

    public ObservableCollection<CausalFactorViewModel> CausalFactors { get; } = [];

    // ── Section 4: What Happens Next? ────────────────────────────────────────

    [ObservableProperty] private Visibility _forecastsVisibility   = Visibility.Collapsed;
    [ObservableProperty] private Visibility _noForecastsVisibility = Visibility.Visible;

    public ObservableCollection<ForecastOutlookViewModel> Forecasts { get; } = [];

    // ── Section 5: Recommendations ───────────────────────────────────────────

    [ObservableProperty] private string     _recommendationsCountText       = "";
    [ObservableProperty] private Visibility _recommendationsVisibility      = Visibility.Collapsed;
    [ObservableProperty] private Visibility _noRecommendationsVisibility    = Visibility.Visible;

    public ObservableCollection<RecommendationViewModel> Recommendations { get; } = [];

    // ── Section 6: Evidence ───────────────────────────────────────────────────

    public ObservableCollection<EvidenceItemViewModel> Evidence { get; } = [];

    // ── Constructor ───────────────────────────────────────────────────────────

    public ExplainViewModel(IExplainMyPcEngine engine)
    {
        _engine = engine;
        _engine.ExplanationUpdated += OnExplanationUpdated;

        // Seed from current explanation if already available
        if (_engine.CurrentExplanation is { } current)
            Apply(current);
    }

    // ── Engine subscription ───────────────────────────────────────────────────

    private void OnExplanationUpdated(object? sender, OperationalExplanation explanation)
        => Apply(explanation);

    // ── Apply explanation to observable state ─────────────────────────────────

    private void Apply(OperationalExplanation exp)
    {
        // ── Section 1: Hero ───────────────────────────────────────────────────
        Headline           = exp.Headline;
        HealthState        = exp.SystemHealthState;
        SummaryNarrative   = exp.SummaryNarrative;
        ConfidencePercent  = exp.OverallConfidencePercent;
        ConfidenceText     = $"{exp.OverallConfidencePercent}% confidence";
        DataPointsText     = $"{exp.DataPointsConsidered:N0} data points";
        LastUpdatedText    = FormatRelativeTime(exp.GeneratedAt);
        bool isHealthy     = exp.HealthStateEnum == NarrativeHealthState.Healthy;
        bool hasIssues     = !isHealthy;

        var (healthBrush, heroBrush, heroGlyph) = StateVisuals(exp.HealthStateEnum);
        HealthStateBrush = healthBrush;
        HeroAccentBrush  = heroBrush;
        HeroGlyph        = heroGlyph;

        DominantCauses.Clear();
        foreach (var cause in exp.DominantCauses)
            DominantCauses.Add(cause);
        DominantCausesVisibility = hasIssues ? Visibility.Visible : Visibility.Collapsed;

        // ── Section 2: What Changed? ──────────────────────────────────────────
        RecentChanges.Clear();
        foreach (var item in exp.RecentChanges)
            RecentChanges.Add(MapRecentChange(item));

        var hasChanges             = exp.RecentChanges.Count > 0;
        RecentChangesVisibility    = hasChanges ? Visibility.Visible : Visibility.Collapsed;
        NoRecentChangesVisibility  = hasChanges ? Visibility.Collapsed : Visibility.Visible;
        RecentChangesLabel         = hasChanges
            ? $"{exp.RecentChanges.Count} change(s) in the last 4 hours"
            : "No significant changes in the last 4 hours";

        // ── Section 3: What's Causing This? ──────────────────────────────────
        CausalFactors.Clear();
        foreach (var factor in exp.CausalFactors)
            CausalFactors.Add(MapCausalFactor(factor));

        var hasCausal              = exp.CausalFactors.Count > 0;
        CausalFactorsVisibility    = hasCausal ? Visibility.Visible : Visibility.Collapsed;
        NoCausalFactorsVisibility  = hasCausal ? Visibility.Collapsed : Visibility.Visible;
        CausalSectionLabel         = hasCausal
            ? $"{exp.CausalFactors.Count} contributing factor(s) identified"
            : "No active conditions identified — all metrics look normal";

        // ── Section 4: Forecasts ──────────────────────────────────────────────
        Forecasts.Clear();
        foreach (var forecast in exp.Forecasts)
            Forecasts.Add(MapForecast(forecast));

        var hasForecasts         = exp.Forecasts.Count > 0;
        ForecastsVisibility      = hasForecasts ? Visibility.Visible : Visibility.Collapsed;
        NoForecastsVisibility    = hasForecasts ? Visibility.Collapsed : Visibility.Visible;

        // ── Section 5: Recommendations ────────────────────────────────────────
        Recommendations.Clear();
        int rank = 1;
        foreach (var rec in exp.Recommendations)
            Recommendations.Add(MapRecommendation(rec, rank++));

        var hasRecs                    = exp.Recommendations.Count > 0;
        RecommendationsVisibility      = hasRecs ? Visibility.Visible : Visibility.Collapsed;
        NoRecommendationsVisibility    = hasRecs ? Visibility.Collapsed : Visibility.Visible;
        RecommendationsCountText       = hasRecs
            ? $"{exp.Recommendations.Count} action(s) recommended"
            : "";

        // ── Section 6: Evidence ───────────────────────────────────────────────
        Evidence.Clear();
        foreach (var ev in exp.Evidence)
            Evidence.Add(MapEvidence(ev));
    }

    // ── Item mappers ──────────────────────────────────────────────────────────

    private static RecentChangeViewModel MapRecentChange(RecentChangeItem item) => new()
    {
        Title      = item.Title,
        Detail     = item.Detail,
        HasDetail           = !string.IsNullOrWhiteSpace(item.Detail),
        HasDetailVisibility = !string.IsNullOrWhiteSpace(item.Detail) ? Visibility.Visible : Visibility.Collapsed,
        TimeText   = FormatRelativeTime(item.OccurredAt),
        TypeGlyph  = TimelineGlyph(item.SourceType),
        DotBrush   = SeverityBrush(item.Severity),
    };

    private static CausalFactorViewModel MapCausalFactor(CausalFactor factor)
    {
        var (label, glyph, brush) = DomainVisuals(factor.Domain);
        var attribution = factor.AttributedProcesses.Count > 0
            ? string.Join(", ", factor.AttributedProcesses.Take(3)
                  .Select(a => $"{a.DisplayName} ({a.UsageText} {a.KindLabel})"))
            : "";

        return new CausalFactorViewModel
        {
            Title             = factor.Title,
            Explanation       = factor.Explanation,
            DomainLabel       = label,
            DomainGlyph       = glyph,
            ConfidencePercent = factor.ConfidencePercent,
            ConfidenceText    = $"{factor.ConfidencePercent}% confidence",
            AttributionText   = attribution,
            HasAttribution           = attribution.Length > 0,
            HasAttributionVisibility = attribution.Length > 0 ? Visibility.Visible : Visibility.Collapsed,
            AccentBrush       = brush,
        };
    }

    private static ForecastOutlookViewModel MapForecast(ForecastOutlook outlook) => new()
    {
        Title             = outlook.Title,
        Projection        = outlook.Projection,
        MitigationHint    = outlook.MitigationHint ?? "",
        HasMitigation           = !string.IsNullOrWhiteSpace(outlook.MitigationHint),
        HasMitigationVisibility = !string.IsNullOrWhiteSpace(outlook.MitigationHint) ? Visibility.Visible : Visibility.Collapsed,
        ConfidenceText          = $"{outlook.ConfidencePercent}% confidence",
        ConfidencePercent = outlook.ConfidencePercent,
        SeverityGlyph     = ForecastGlyph(outlook.Severity),
        SeverityBrush     = ForecastBrush(outlook.Severity),
    };

    private static RecommendationViewModel MapRecommendation(RecommendationReasoning rec, int rank)
    {
        // Effectiveness badge (cold-start safe — returns null before first analysis)
        var profile = AppServices.LearningService.GetProfile(rec.Action.Id);
        string effLabel    = "";
        string effText     = "";
        var    effVisible  = Visibility.Collapsed;
        string effBrushKey = "TextSecondaryBrush";

        if (profile is { IsWarmEnough: true })
        {
            effLabel    = profile.EffectivenessLabel;
            effText     = profile.SummaryText;
            effVisible  = Visibility.Visible;
            effBrushKey = profile.EffectivenessRate >= 0.70
                ? "StatusGoodBrush"
                : profile.EffectivenessRate >= 0.30
                    ? "StatusModerateBrush"
                    : "StatusCriticalBrush";
        }

        return new RecommendationViewModel
        {
            Rank                 = rank,
            ActionTitle          = rec.Action.Title,
            WhyItMatters         = rec.WhyItMatters,
            ExpectedBenefit      = rec.ExpectedBenefit,
            EstimatedTime        = rec.EstimatedTime,
            ConfidencePercent    = rec.ConfidencePercent,
            CanRollback          = rec.CanRollback,
            RollbackLabel        = rec.CanRollback ? "Reversible" : "Not reversible",
            RiskLabel            = RiskLabel(rec.Action.Risk),
            RankBrush            = rank == 1
                ? new SolidColorBrush(Color.FromArgb(255, 108, 163, 248))   // accent blue
                : new SolidColorBrush(Color.FromArgb(255, 60, 70, 90)),      // subtle
            EffectivenessLabel    = effLabel,
            EffectivenessText     = effText,
            EffectivenessVisible  = effVisible,
            EffectivenessBrushKey = effBrushKey,
        };
    }

    private static EvidenceItemViewModel MapEvidence(EvidenceItem item) => new()
    {
        Label     = item.Label,
        Value     = item.Value,
        KindLabel = EvidenceKindLabel(item.Kind),
        KindBrush = EvidenceKindBrush(item.Kind),
    };

    // ── Visual helpers ────────────────────────────────────────────────────────

    private static (Brush health, Brush hero, string glyph) StateVisuals(NarrativeHealthState state) =>
        state switch
        {
            NarrativeHealthState.Healthy    => (
                new SolidColorBrush(Color.FromArgb(255, 76,  175, 80)),   // green
                new SolidColorBrush(Color.FromArgb(255, 76,  175, 80)),
                ""),   // checkmark
            NarrativeHealthState.Monitoring => (
                new SolidColorBrush(Color.FromArgb(255, 108, 163, 248)),  // blue
                new SolidColorBrush(Color.FromArgb(255, 108, 163, 248)),
                ""),   // insights
            NarrativeHealthState.Degraded   => (
                new SolidColorBrush(Color.FromArgb(255, 255, 167, 38)),   // amber
                new SolidColorBrush(Color.FromArgb(255, 255, 167, 38)),
                ""),   // warning
            NarrativeHealthState.Critical   => (
                new SolidColorBrush(Color.FromArgb(255, 239, 83,  80)),   // red
                new SolidColorBrush(Color.FromArgb(255, 239, 83,  80)),
                ""),   // error badge
            _ => (
                new SolidColorBrush(Colors.Gray),
                new SolidColorBrush(Colors.Gray),
                ""),
        };

    private static (string label, string glyph, Brush brush) DomainVisuals(CausalDomain domain) =>
        domain switch
        {
            CausalDomain.Cpu      => ("CPU",     "", new SolidColorBrush(Color.FromArgb(255, 108, 163, 248))),
            CausalDomain.Memory   => ("Memory",  "", new SolidColorBrush(Color.FromArgb(255, 108, 163, 248))),
            CausalDomain.Disk     => ("Disk",    "", new SolidColorBrush(Color.FromArgb(255, 255, 167, 38))),
            CausalDomain.Startup  => ("Startup", "", new SolidColorBrush(Color.FromArgb(255, 255, 167, 38))),
            CausalDomain.Thermal  => ("Thermal", "", new SolidColorBrush(Color.FromArgb(255, 239, 83,  80))),
            CausalDomain.Security => ("Security","", new SolidColorBrush(Color.FromArgb(255, 108, 163, 248))),
            CausalDomain.Process  => ("Process", "", new SolidColorBrush(Color.FromArgb(255, 108, 163, 248))),
            _                     => ("System",  "", new SolidColorBrush(Colors.Gray)),
        };

    private static Brush SeverityBrush(TimelineEventSeverity severity) => severity switch
    {
        TimelineEventSeverity.Good     => new SolidColorBrush(Color.FromArgb(255, 76,  175, 80)),
        TimelineEventSeverity.Warning  => new SolidColorBrush(Color.FromArgb(255, 255, 167, 38)),
        TimelineEventSeverity.Critical => new SolidColorBrush(Color.FromArgb(255, 239, 83,  80)),
        _                              => new SolidColorBrush(Color.FromArgb(255, 108, 163, 248)),
    };

    private static string TimelineGlyph(TimelineEventType type) => type switch
    {
        TimelineEventType.InsightOnset       => "",
        TimelineEventType.InsightResolved    => "",
        TimelineEventType.ForecastAlert      => "",
        TimelineEventType.ActionExecuted     => "",
        TimelineEventType.ActionRollback     => "",
        TimelineEventType.ActionFailed       => "",
        TimelineEventType.SynthesisCorrelated=> "",
        TimelineEventType.NarrativeCheckpoint=> "",
        _                                    => "",
    };

    private static string ForecastGlyph(ForecastSeverity severity) => severity switch
    {
        ForecastSeverity.Urgent  => "",
        ForecastSeverity.Warning => "",
        ForecastSeverity.Watch   => "",
        _                        => "",
    };

    private static Brush ForecastBrush(ForecastSeverity severity) => severity switch
    {
        ForecastSeverity.Urgent  => new SolidColorBrush(Color.FromArgb(255, 239, 83,  80)),
        ForecastSeverity.Warning => new SolidColorBrush(Color.FromArgb(255, 255, 167, 38)),
        ForecastSeverity.Watch   => new SolidColorBrush(Color.FromArgb(255, 108, 163, 248)),
        _                        => new SolidColorBrush(Color.FromArgb(255, 76,  175, 80)),
    };

    private static string RiskLabel(ActionRisk risk) => risk switch
    {
        ActionRisk.Safe     => "Safe",
        ActionRisk.Low      => "Low risk",
        ActionRisk.Moderate => "Moderate risk",
        ActionRisk.High     => "High risk",
        _                   => "Unknown risk",
    };

    private static string EvidenceKindLabel(EvidenceKind kind) => kind switch
    {
        EvidenceKind.DirectMeasurement  => "Measured",
        EvidenceKind.BaselineComparison => "Baseline",
        EvidenceKind.TrendObservation   => "Trend",
        EvidenceKind.ProcessAttribution => "Attribution",
        EvidenceKind.TimelineCorrelation=> "Timeline",
        _                               => "Data",
    };

    private static Brush EvidenceKindBrush(EvidenceKind kind) => kind switch
    {
        EvidenceKind.BaselineComparison => new SolidColorBrush(Color.FromArgb(255, 108, 163, 248)),
        EvidenceKind.ProcessAttribution => new SolidColorBrush(Color.FromArgb(255, 255, 167, 38)),
        EvidenceKind.TrendObservation   => new SolidColorBrush(Color.FromArgb(255, 76,  175, 80)),
        EvidenceKind.TimelineCorrelation=> new SolidColorBrush(Color.FromArgb(255, 108, 163, 248)),
        _                               => new SolidColorBrush(Color.FromArgb(255, 100, 110, 130)),
    };

    private static string FormatRelativeTime(DateTimeOffset t)
    {
        var age = DateTimeOffset.Now - t;
        if (age.TotalSeconds < 10)  return "just now";
        if (age.TotalSeconds < 60)  return $"{(int)age.TotalSeconds}s ago";
        if (age.TotalMinutes < 60)  return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalHours   < 24)  return $"{(int)age.TotalHours}h ago";
        return t.ToString("MMM d");
    }
}
