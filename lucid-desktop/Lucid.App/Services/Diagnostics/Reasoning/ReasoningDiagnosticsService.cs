using System.Collections.Concurrent;
using Lucid.Services.Infrastructure.Events;
using Lucid.Services.Reasoning.Cognitive;

namespace Lucid.Services.Diagnostics.Reasoning;

// ── Event ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Published after each cognitive trace record is committed.
/// Allows the diagnostics page to refresh without polling.
/// </summary>
public sealed record CognitiveTraceCommittedEvent(
    int ActiveInferenceCount,
    double DurationMs) : OperationalEvent
{
    public static CognitiveTraceCommittedEvent From(CognitiveTraceRecord trace) =>
        new(trace.ActiveInferenceCount, trace.DurationMs)
        {
            Source   = "ReasoningDiagnostics",
            Priority = EventPriority.Low,
        };
}

// ── Service ───────────────────────────────────────────────────────────────────

/// <summary>
/// Collects and exposes reasoning observability data: cognitive cycle traces,
/// arbitration decision logs, and aggregate health metrics.
///
/// This service is the "X-ray" of the reasoning pipeline — it makes the
/// reasoning pipeline fully transparent and debuggable.
///
/// Subscribers:
///   - DiagnosticsPageViewModel — displays cycle history and suppression rates
///   - ReasoningHealthMetrics — aggregates throughput and latency data
///
/// Thread-safe: all collections are concurrent.
/// </summary>
public sealed class ReasoningDiagnosticsService
{
    private const int MaxCycleHistory       = 50;
    private const int MaxDecisionHistory    = 500;

    private readonly IEventBus _eventBus;

    private readonly ConcurrentQueue<CognitiveTraceRecord>       _cycles    = new();
    private readonly ConcurrentQueue<ArbitrationDecisionRecord>  _decisions = new();

    public ReasoningDiagnosticsService(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    // ── Recording ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Records a completed cognitive reasoning cycle.
    /// Publishes a <see cref="CognitiveTraceCommittedEvent"/> after recording.
    /// </summary>
    public void RecordCycle(CognitiveTraceRecord trace)
    {
        _cycles.Enqueue(trace);
        while (_cycles.Count > MaxCycleHistory)
            _cycles.TryDequeue(out _);

        _ = _eventBus.PublishAsync(CognitiveTraceCommittedEvent.From(trace));
    }

    /// <summary>
    /// Records one arbitration decision (pass or suppress) for a single action.
    /// </summary>
    public void RecordArbitrationDecision(ArbitrationDecisionRecord decision)
    {
        _decisions.Enqueue(decision);
        while (_decisions.Count > MaxDecisionHistory)
            _decisions.TryDequeue(out _);
    }

    // ── Observation ───────────────────────────────────────────────────────────

    /// <summary>Returns the most recent cognitive cycle traces, newest first.</summary>
    public IReadOnlyList<CognitiveTraceRecord> GetRecentCycles(int count = 10) =>
        [.. _cycles.Reverse().Take(count)];

    /// <summary>Returns the most recent arbitration decisions, newest first.</summary>
    public IReadOnlyList<ArbitrationDecisionRecord> GetRecentDecisions(int count = 50) =>
        [.. _decisions.Reverse().Take(count)];

    /// <summary>Total cycles recorded this session.</summary>
    public int TotalCycles => _cycles.Count;

    /// <summary>
    /// Suppression rate across all arbitration decisions (0.0–1.0).
    /// Returns 0 when no decisions have been recorded.
    /// </summary>
    public double SuppressionRate
    {
        get
        {
            var decisions = _decisions.ToArray();
            if (decisions.Length == 0) return 0;
            var suppressed = decisions.Count(d => !d.Passed);
            return (double)suppressed / decisions.Length;
        }
    }

    /// <summary>
    /// Average inference count per cycle over the last <paramref name="n"/> cycles.
    /// </summary>
    public double AverageInferencesPerCycle(int n = 10)
    {
        var recent = _cycles.TakeLast(n).ToList();
        return recent.Count == 0 ? 0 : recent.Average(c => c.ActiveInferenceCount);
    }

    /// <summary>
    /// Average cycle duration in milliseconds over the last <paramref name="n"/> cycles.
    /// </summary>
    public double AverageCycleDurationMs(int n = 10)
    {
        var recent = _cycles.TakeLast(n).ToList();
        return recent.Count == 0 ? 0 : recent.Average(c => c.DurationMs);
    }

    /// <summary>
    /// Average confidence score across the last <paramref name="n"/> cycles.
    /// </summary>
    public double AverageConfidence(int n = 10)
    {
        var recent = _cycles.TakeLast(n).ToList();
        return recent.Count == 0 ? 0 : recent.Average(c => c.MaxConfidence);
    }
}
