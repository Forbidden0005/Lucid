using System.Collections.Concurrent;
using Lucid.Services.Infrastructure.Events;
using Lucid.Services.Interaction.Attention;
using Lucid.Services.Interaction.Recommendations;

namespace Lucid.Services.Interaction.Diagnostics;

// ── Event ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Published when a recommendation is suppressed by the attention pipeline.
/// Allows the Diagnostics page to update without polling.
/// </summary>
public sealed record InteractionSuppressionEvent(
    Guid   ClusterId,
    string ActionKey,
    string Reason) : OperationalEvent
{
    public static InteractionSuppressionEvent From(
        UnifiedRecommendation rec,
        AttentionDecision     decision) =>
        new(rec.ClusterId,
            rec.SourceCluster?.LeadAction?.Action?.Id ?? rec.ClusterId.ToString(),
            decision.SuppressReason ?? "Unknown reason")
        {
            Source   = "InteractionDiagnostics",
            Priority = EventPriority.Low,
        };
}

// ── Service ────────────────────────────────────────────────────────────────────

/// <summary>
/// Collects and exposes interaction-layer observability data:
///   - Suppression records (why items were filtered)
///   - Aggregate health metrics (surfacing rates, latency)
///   - Attention coordinator decisions
///
/// This service is the transparency backbone of the interaction pipeline —
/// every suppression is recorded here so the user can always discover
/// "Why was this recommendation not shown to me?"
///
/// Thread-safe: all state is in concurrent collections or Interlocked fields.
/// </summary>
public sealed class InteractionDiagnosticsService
{
    private const int MaxSuppressionHistory = 200;

    private readonly IEventBus                          _eventBus;
    private readonly InteractionHealthMetrics           _metrics;
    private readonly ConcurrentQueue<SuppressionRecord> _suppressions = new();

    public InteractionDiagnosticsService(
        IEventBus                eventBus,
        InteractionHealthMetrics metrics)
    {
        _eventBus = eventBus;
        _metrics  = metrics;
    }

    // ── Recording ──────────────────────────────────────────────────────────────

    /// <summary>Records a suppression decision and publishes a diagnostic event.</summary>
    public void RecordSuppression(
        UnifiedRecommendation recommendation,
        AttentionDecision     decision)
    {
        var record = new SuppressionRecord(
            ClusterId:    recommendation.ClusterId,
            Headline:     recommendation.Headline,
            SuppressReason: decision.SuppressReason ?? "Unknown",
            OccurredAt:   DateTime.UtcNow);

        _suppressions.Enqueue(record);
        while (_suppressions.Count > MaxSuppressionHistory)
            _suppressions.TryDequeue(out _);

        _metrics.RecordSuppressed();

        _ = _eventBus.PublishAsync(
            InteractionSuppressionEvent.From(recommendation, decision));
    }

    /// <summary>Records that a recommendation was surfaced to the user.</summary>
    public void RecordSurfaced() => _metrics.RecordSurfaced();

    /// <summary>Records a budget exhaustion event.</summary>
    public void RecordBudgetExhausted() => _metrics.RecordBudgetExhausted();

    // ── Observation ────────────────────────────────────────────────────────────

    /// <summary>Returns recent suppression records, newest first.</summary>
    public IReadOnlyList<SuppressionRecord> GetRecentSuppressions(int count = 20) =>
        [.. _suppressions.Reverse().Take(count)];

    /// <summary>Live interaction health metrics.</summary>
    public InteractionHealthMetrics Metrics => _metrics;

    /// <summary>Total suppression events recorded this session.</summary>
    public int TotalSuppressions => _suppressions.Count;
}

/// <summary>
/// Immutable record of a single suppression event.
/// Used for the "Why was this recommendation hidden?" transparency panel.
/// </summary>
public sealed record SuppressionRecord(
    Guid     ClusterId,
    string   Headline,
    string   SuppressReason,
    DateTime OccurredAt);
