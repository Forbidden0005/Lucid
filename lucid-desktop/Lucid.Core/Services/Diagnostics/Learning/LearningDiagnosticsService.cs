using System.Collections.Concurrent;
using Lucid.Services.Infrastructure.Events;

namespace Lucid.Services.Diagnostics.Learning;

// ── Event ──────────────────────────────────────────────────────────────────────

/// <summary>Published after each adaptive learning event is recorded.</summary>
public sealed record LearningTraceCommittedEvent(
    LearningEventType EventType,
    string            Description) : OperationalEvent
{
    public static LearningTraceCommittedEvent From(LearningTraceRecord trace) =>
        new(trace.EventType, trace.Description)
        {
            Source   = "LearningDiagnostics",
            Priority = EventPriority.Low,
        };
}

// ── Service ────────────────────────────────────────────────────────────────────

/// <summary>
/// Aggregates adaptive learning trace records and publishes learning events.
///
/// This service is the transparency backbone of Phase 21 — every adaptation
/// Lucid makes is recorded here so users can always inspect "what has Lucid
/// learned about my machine?"
///
/// Consumed by:
///   - DiagnosticsPageViewModel — "What has Lucid learned?" panel
///   - Explainability layer     — links learning events to operational decisions
///
/// Thread-safe: ConcurrentQueue-backed, max 300 traces retained in memory.
/// </summary>
public sealed class LearningDiagnosticsService
{
    private const int MaxTraces = 300;

    private readonly IEventBus                          _eventBus;
    private readonly ConcurrentQueue<LearningTraceRecord> _traces = new();

    public LearningDiagnosticsService(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    // ── Recording ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Records an adaptive learning event and publishes a committed event to the bus.
    /// </summary>
    public void Record(LearningTraceRecord trace)
    {
        _traces.Enqueue(trace);

        while (_traces.Count > MaxTraces)
            _traces.TryDequeue(out _);

        _ = _eventBus.PublishAsync(LearningTraceCommittedEvent.From(trace));
    }

    /// <summary>Convenience factory: records a learning event from component parts.</summary>
    public void Record(
        LearningEventType eventType,
        string            description,
        string?           evidenceSummary  = null,
        double            confidence       = 0.5,
        string?           relatedEntityId  = null,
        string?           governanceNote   = null)
    {
        Record(new LearningTraceRecord
        {
            EventType         = eventType,
            Description       = description,
            EvidenceSummary   = evidenceSummary,
            LearningConfidence = confidence,
            RelatedEntityId   = relatedEntityId,
            GovernanceNote    = governanceNote,
        });
    }

    // ── Observation ────────────────────────────────────────────────────────────

    /// <summary>Returns the most recent learning traces, newest first.</summary>
    public IReadOnlyList<LearningTraceRecord> GetRecentTraces(int count = 20) =>
        [.. _traces.Reverse().Take(count)];

    /// <summary>Returns traces filtered by event type, newest first.</summary>
    public IReadOnlyList<LearningTraceRecord> GetByType(
        LearningEventType type,
        int               count = 20) =>
        _traces.Reverse()
               .Where(t => t.EventType == type)
               .Take(count)
               .ToList()
               .AsReadOnly();

    /// <summary>Total learning events recorded this session.</summary>
    public int TotalEventCount => _traces.Count;

    /// <summary>Count of events by type for the diagnostics summary panel.</summary>
    public IReadOnlyDictionary<LearningEventType, int> EventCountsByType()
    {
        return _traces
            .GroupBy(t => t.EventType)
            .ToDictionary(g => g.Key, g => g.Count());
    }
}
