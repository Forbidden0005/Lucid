using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Intelligence.Rules;

namespace ExplainMyPC.Services.Intelligence;

/// <summary>
/// Runs registered <see cref="IInsightRule"/> heuristics on every telemetry
/// tick and publishes findings when the active set changes.
///
/// Rule evaluation:
///   All rules are evaluated synchronously on the UI thread (telemetry
///   readings are already marshalled there). Each rule call is ≤1 µs —
///   a history GetStats() is a single locked linear scan over ≤1200 doubles.
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
///   Add a new IInsightRule to CreateRules(). No other changes needed.
/// </summary>
public sealed class SystemInsightEngine : ISystemInsightEngine
{
    private readonly ITelemetryService       _telemetry;
    private readonly ITelemetryHistoryBuffer _history;
    private readonly IReadOnlyList<IInsightRule> _rules;

    // Tracks (Id, Severity) from the last InsightsUpdated notification.
    // Comparing this set against the freshly-evaluated set determines
    // whether the UI needs to be told about a change.
    private HashSet<(string Id, InsightSeverity Severity)> _lastSignature = [];

    public event EventHandler<IReadOnlyList<SystemInsight>>? InsightsUpdated;
    public IReadOnlyList<SystemInsight> CurrentInsights { get; private set; } = [];

    // ── Construction ──────────────────────────────────────────────────────────

    public SystemInsightEngine(ITelemetryService telemetry, ITelemetryHistoryBuffer history)
    {
        _telemetry = telemetry;
        _history   = history;
        _rules     = CreateRules();
    }

    /// <summary>
    /// The ordered list of heuristics evaluated each tick.
    /// SystemRunningWellRule is last so it only fires when no other
    /// conditions would logically precede an "all-clear" message.
    /// </summary>
    private static IReadOnlyList<IInsightRule> CreateRules() =>
    [
        new SustainedHighCpuRule(),
        new ElevatedRamPressureRule(),
        new AbnormalGpuUsageRule(),
        new LowDiskSpaceRule(),
        new HighDiskThroughputRule(),
        new HighCpuTemperatureRule(),
        new SystemRunningWellRule(),   // "all clear" — intentionally last
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
        var results = new List<SystemInsight>(_rules.Count);

        foreach (var rule in _rules)
        {
            try
            {
                var insight = rule.Evaluate(snapshot, _history);
                if (insight is not null)
                    results.Add(insight);
            }
            catch
            {
                // A crashing rule must never surface to the UI or break other rules.
                // The finding is silently omitted for this evaluation cycle.
            }
        }

        // Highest severity first so the most actionable findings lead the list.
        results.Sort(static (a, b) =>
            ((int)b.Severity).CompareTo((int)a.Severity));

        return results;
    }
}
