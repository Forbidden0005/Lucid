using Lucid.Services.Reasoning.Cognitive;

namespace Lucid.Services.Reasoning.Memory;

/// <summary>
/// Maintains a lightweight, additive history of operational inferences and
/// their observed outcomes for pattern detection and confidence calibration.
///
/// Design principles:
///   - Additive only: entries are never modified after recording
///   - Bounded: old entries are pruned to avoid unbounded growth
///   - Explainable: every stored field has a clear operational purpose
///   - Privacy-safe: no user-identifiable data is ever stored
///   - No hidden profiling: all stored data is inspectable via the Diagnostics page
///
/// Thread-safe: all methods are safe for concurrent callers.
/// </summary>
public interface IReasoningMemoryService
{
    // ── Recording ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Records a new inference in memory.
    /// Called by the cognitive reasoning engine after each reasoning cycle
    /// for inferences with priority ≥ <see cref="InferencePriority.Advisory"/>.
    /// </summary>
    void Record(OperationalInference inference);

    /// <summary>
    /// Updates the outcome observation for a previously recorded inference.
    /// Called when the system state changes in a way that validates or
    /// contradicts an earlier inference.
    /// </summary>
    void UpdateOutcome(Guid inferenceId, bool wasValidated, bool userActed = false);

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the most recent N memory entries in reverse-chronological order.
    /// </summary>
    IReadOnlyList<ReasoningMemoryEntry> GetRecent(int maxCount = 100);

    /// <summary>
    /// Returns entries for the given domain over the specified time window.
    /// Used to detect recurring patterns.
    /// </summary>
    IReadOnlyList<ReasoningMemoryEntry> GetByDomain(
        string   domain,
        TimeSpan window);

    /// <summary>
    /// Returns the historical validation rate for the given domain (0–1).
    /// Returns null if fewer than <paramref name="minSamples"/> are available.
    /// Used to adjust confidence weights for future inferences.
    /// </summary>
    double? GetValidationRate(string domain, int minSamples = 5);

    /// <summary>
    /// Returns the count of times the given domain has been inferred as elevated
    /// within the specified time window.
    /// Used to detect chronic vs. transient patterns.
    /// </summary>
    int GetRecurrenceCount(string domain, TimeSpan window);

    // ── Maintenance ───────────────────────────────────────────────────────────

    /// <summary>
    /// Prunes entries older than <paramref name="retentionWindow"/>.
    /// Called by the lifecycle coordinator during maintenance windows.
    /// Default retention is 30 days.
    /// </summary>
    void Prune(TimeSpan? retentionWindow = null);
}
