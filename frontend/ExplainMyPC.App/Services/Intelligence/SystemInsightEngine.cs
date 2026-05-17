using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Intelligence.Rules;

namespace ExplainMyPC.Services.Intelligence;

/// <summary>
/// Runs registered <see cref="IInsightRule"/> heuristics on every telemetry
/// tick and publishes findings when the active set changes.
///
/// Evaluation pipeline:
///   Phase 1 — Base rules: each <see cref="IInsightRule"/> examines the current
///     telemetry snapshot and produces an insight if its threshold is met.
///     All base insights carry process attributions when process samples are
///     available (~4.5 s warm-up after startup).
///
///   Phase 2 — Synthesis: <see cref="InsightSynthesisEngine"/> examines the
///     full set of base findings and runs <see cref="ISynthesisRule"/> heuristics
///     that look for cross-insight patterns (e.g. the same process attributed as
///     the cause of both CPU and RAM pressure). Synthesized insights are appended
///     to the active set with IDs starting with "synthesis.".
///
///   Phase 3 — Sort and publish: combined list sorted highest severity first.
///     InsightsUpdated fires only when the (Id, Severity) fingerprint changes.
///
/// Rule evaluation:
///   All phases run synchronously on the UI thread (telemetry readings are
///   already marshalled there). Each base rule call is ≤1 µs. Synthesis is
///   pure LINQ over in-memory lists — also ≤1 µs per rule.
///
/// Change detection:
///   A compact signature of (Id, Severity) pairs tracks the previous
///   notification. InsightsUpdated fires only when the signature changes,
///   so the dashboard badge and count update exactly when findings
///   appear or disappear — not on every poll tick.
///
/// Fault isolation:
///   Exceptions inside individual rules are silently swallowed. A bad rule
///   is omitted for that tick; it cannot crash the engine or other rules.
///
/// Extending:
///   Base rules: add a new IInsightRule to CreateRules().
///   Synthesis rules: add a new ISynthesisRule to CreateSynthesisRules().
///   No other changes needed in either case.
/// </summary>
public sealed class SystemInsightEngine : ISystemInsightEngine
{
    private readonly ITelemetryService           _telemetry;
    private readonly ITelemetryHistoryBuffer     _history;
    private readonly IReadOnlyList<IInsightRule> _rules;
    private readonly InsightSynthesisEngine      _synthesizer;

    // Tracks (Id, Severity) from the last InsightsUpdated notification.
    // Comparing this set against the freshly-evaluated set determines
    // whether the UI needs to be told about a change.
    private HashSet<(string Id, InsightSeverity Severity)> _lastSignature = [];

    public event EventHandler<IReadOnlyList<SystemInsight>>? InsightsUpdated;
    public IReadOnlyList<SystemInsight> CurrentInsights { get; private set; } = [];

    // ── Construction ──────────────────────────────────────────────────────────

    public SystemInsightEngine(ITelemetryService telemetry, ITelemetryHistoryBuffer history)
    {
        _telemetry   = telemetry;
        _history     = history;
        _rules       = CreateRules();
        _synthesizer = new InsightSynthesisEngine(CreateSynthesisRules());
    }

    /// <summary>
    /// Phase 1 heuristics — evaluated against each telemetry snapshot.
    ///
    /// Order within severity groups matters for tie-breaking only;
    /// <see cref="EvaluateAll"/> re-sorts the final list by severity.
    ///
    /// Point-in-time rules (fire when a threshold is currently exceeded):
    ///   SustainedHighCpuRule, ElevatedRamPressureRule, AbnormalGpuUsageRule,
    ///   LowDiskSpaceRule, HighDiskThroughputRule, HighCpuTemperatureRule
    ///
    /// Trend-aware rules (fire when a metric is changing over time):
    ///   RisingRamTrendRule, CpuEscalationRule, PeriodicCpuSpikeRule,
    ///   WorseningThermalTrendRule, LongRunningDiskPressureRule
    ///
    /// SystemRunningWellRule is last — only fires when no other rule produces a finding.
    /// </summary>
    private static IReadOnlyList<IInsightRule> CreateRules() =>
    [
        // ── Point-in-time rules ────────────────────────────────────────────────
        new SustainedHighCpuRule(),
        new ElevatedRamPressureRule(),
        new AbnormalGpuUsageRule(),
        new LowDiskSpaceRule(),
        new HighDiskThroughputRule(),
        new HighCpuTemperatureRule(),

        // ── Trend-aware rules ─────────────────────────────────────────────────
        // These require history window coverage before they can fire.
        // On cold start they return null until enough samples accumulate.
        new RisingRamTrendRule(),
        new CpuEscalationRule(),
        new PeriodicCpuSpikeRule(),
        new WorseningThermalTrendRule(),
        new LongRunningDiskPressureRule(),

        // ── All-clear ─────────────────────────────────────────────────────────
        new SystemRunningWellRule(),   // only fires when every rule above is silent
    ];

    /// <summary>
    /// Phase 2 heuristics — evaluated against the full set of active base findings.
    /// Add new ISynthesisRule implementations here as cross-insight patterns are identified.
    /// </summary>
    private static IReadOnlyList<ISynthesisRule> CreateSynthesisRules() =>
    [
        new SharedProcessSynthesisRule(),   // same process in 2+ distinct insights
    ];

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Start() => _telemetry.ReadingAvailable += OnReadingAvailable;
    public void Stop()  => _telemetry.ReadingAvailable -= OnReadingAvailable;
    public void Dispose() => Stop();

    // ── Evaluation ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called on the UI thread for every telemetry snapshot.
    /// Evaluates all rules and publishes if findings changed.
    /// </summary>
    private void OnReadingAvailable(object? sender, TelemetrySnapshot snapshot)
    {
        var insights = EvaluateAll(snapshot);
        CurrentInsights = insights;

        // Build a compact fingerprint of the active findings.
        var signature = new HashSet<(string, InsightSeverity)>(
            insights.Select(static i => (i.Id, i.Severity)));

        if (!signature.SetEquals(_lastSignature))
        {
            _lastSignature = signature;
            InsightsUpdated?.Invoke(this, insights);
        }
    }

    private List<SystemInsight> EvaluateAll(TelemetrySnapshot snapshot)
    {
        // ── Phase 1: base rule evaluation ─────────────────────────────────────
        var baseResults = new List<SystemInsight>(_rules.Count);

        foreach (var rule in _rules)
        {
            try
            {
                var insight = rule.Evaluate(snapshot, _history);
                if (insight is not null)
                    baseResults.Add(insight);
            }
            catch
            {
                // A crashing rule must never surface to the UI or break other rules.
                // The finding is silently omitted for this evaluation cycle.
            }
        }

        // ── Phase 2: cross-insight synthesis ──────────────────────────────────
        // Pass only base findings so synthesis rules never see stale synthesis.
        var synthesized = _synthesizer.Synthesize(baseResults);

        // ── Phase 3: combine and sort ─────────────────────────────────────────
        var combined = new List<SystemInsight>(baseResults.Count + synthesized.Count);
        combined.AddRange(baseResults);
        combined.AddRange(synthesized);

        // Highest severity first so the most actionable findings lead the list.
        // Synthesis insights sort at their max-contributor severity, naturally
        // surfacing above or beside the individual findings they explain.
        combined.Sort(static (a, b) =>
            ((int)b.Severity).CompareTo((int)a.Severity));

        return combined;
    }
}
