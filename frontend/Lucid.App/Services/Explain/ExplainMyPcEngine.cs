using Lucid.Services.Baseline;
using Lucid.Services.Intelligence;
using Lucid.Services.Narrative;
using Lucid.Services.Timeline;

namespace Lucid.Services.Explain;

/// <summary>
/// Lucid operational reasoning engine — the core of the flagship feature.
///
/// Orchestrates all reasoning sub-systems to produce a single coherent
/// <see cref="OperationalExplanation"/> that answers the user's core questions:
///   • Why is my PC slow right now?
///   • What changed recently?
///   • What is causing this?
///   • What happens if I ignore it?
///   • What should I safely do next?
///
/// Data flow:
///   NarrativeUpdated / NewEventAdded
///     → Rebuild()
///       → SystemStateClassifier   (health state + dominant causes)
///       → OperationalReasoningGraph (causal factors)
///       → ExplanationComposer     (what changed, forecasts, evidence)
///       → RecommendationRanker    (ranked actions)
///     → ExplanationUpdated (UI thread)
///
/// Threading:
///   All upstream events fire on the UI thread. Rebuild() runs synchronously
///   on the UI thread. CurrentExplanation is safe to read without locking.
///
/// Determinism:
///   All output is produced by deterministic template rules. No LLM, no cloud,
///   no randomness. The same inputs always produce the same output.
/// </summary>
internal sealed class ExplainMyPcEngine : IExplainMyPcEngine
{
    private readonly ISystemInsightEngine        _intelligence;
    private readonly ITimelineAggregationService _timeline;
    private readonly IOperationalNarrativeEngine _narrative;
    private readonly ISystemBaselineService      _baseline;
    private readonly ITelemetryHistoryBuffer     _history;

    private bool _running;

    public OperationalExplanation? CurrentExplanation { get; private set; }
    public event EventHandler<OperationalExplanation>? ExplanationUpdated;

    public ExplainMyPcEngine(
        ISystemInsightEngine        intelligence,
        ITimelineAggregationService timeline,
        IOperationalNarrativeEngine narrative,
        ISystemBaselineService      baseline,
        ITelemetryHistoryBuffer     history)
    {
        _intelligence = intelligence;
        _timeline     = timeline;
        _narrative    = narrative;
        _baseline     = baseline;
        _history      = history;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Start()
    {
        if (_running) return;
        _running = true;

        _narrative.NarrativeUpdated += OnNarrativeUpdated;
        _timeline.NewEventAdded     += OnTimelineEventAdded;

        // If intelligence has already produced findings, seed immediately
        // so the page renders something useful before the next insight cycle.
        if (_narrative.CurrentNarrative is not null)
            Rebuild();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;

        _narrative.NarrativeUpdated -= OnNarrativeUpdated;
        _timeline.NewEventAdded     -= OnTimelineEventAdded;
    }

    // ── Upstream event handlers ───────────────────────────────────────────────

    private void OnNarrativeUpdated(object? sender, OperationalNarrative _) => Rebuild();

    private void OnTimelineEventAdded(object? sender, TimelineEvent ev)
    {
        // Skip low-signal session events to avoid unnecessary rebuilds
        if (ev.Type is TimelineEventType.IdleBegin or TimelineEventType.IdleEnd)
            return;
        Rebuild();
    }

    // ── Core reasoning loop ───────────────────────────────────────────────────

    private void Rebuild()
    {
        var insights  = _intelligence.CurrentInsights;
        var narrative = _narrative.CurrentNarrative;
        var events    = _timeline.Events;
        var baseline  = _baseline.CurrentBaseline;

        // Guard: nothing to explain yet
        if (insights == null || insights.Count == 0) return;

        // ── 1. Classify overall system state ─────────────────────────────────
        var (state, stateLabel, dominantCauses, confidence) =
            SystemStateClassifier.Classify(insights);

        // ── 2. Determine headline and narrative prose ─────────────────────────
        var headline = narrative?.Headline ?? HeadlineFor(state);

        // Use up to the first two narrative paragraphs as the summary
        var summaryNarrative = narrative is not null && narrative.Paragraphs.Length > 0
            ? string.Join(" ", narrative.Paragraphs.Take(2))
            : "Analyzing your system — please wait a moment.";

        // ── 3. Build causal factors (What's Causing This?) ───────────────────
        var causalFactors = OperationalReasoningGraph.Build(insights);

        // ── 4. Extract recent changes (What Changed?) ─────────────────────────
        var recentChanges = ExplanationComposer.ComposeRecentChanges(events);

        // ── 5. Extract forecasts (What Happens Next?) ─────────────────────────
        var forecasts = ExplanationComposer.ComposeForecasts(insights);

        // ── 6. Rank recommendations ───────────────────────────────────────────
        var recommendations = RecommendationRanker.Rank(insights);

        // ── 7. Build evidence set (Why We Believe This) ───────────────────────
        var evidence = ExplanationComposer.ComposeEvidence(
            insights, baseline, _history.Count);

        // ── 8. Assemble final explanation ─────────────────────────────────────
        var explanation = new OperationalExplanation(
            SystemHealthState:       stateLabel,
            Headline:                headline,
            OverallConfidencePercent: confidence,
            SummaryNarrative:        summaryNarrative,
            DominantCauses:          dominantCauses,
            RecentChanges:           recentChanges,
            CausalFactors:           causalFactors,
            Forecasts:               forecasts,
            Recommendations:         recommendations,
            Evidence:                evidence,
            HealthStateEnum:         state,
            GeneratedAt:             DateTimeOffset.Now,
            DataPointsConsidered:    _history.Count + insights.Count + events.Count);

        CurrentExplanation = explanation;
        ExplanationUpdated?.Invoke(this, explanation);
    }

    // ── Fallback headline ─────────────────────────────────────────────────────

    private static string HeadlineFor(Narrative.SystemHealthState state) => state switch
    {
        Narrative.SystemHealthState.Healthy    => "Your system is running well.",
        Narrative.SystemHealthState.Monitoring => "Your system has a few items worth reviewing.",
        Narrative.SystemHealthState.Degraded   => "Your system has conditions that need attention.",
        Narrative.SystemHealthState.Critical   => "Your system has multiple active issues.",
        _                                      => "Analyzing your system…",
    };
}
