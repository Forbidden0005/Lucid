using Lucid.Services.Reasoning.Cognitive;

namespace Lucid.Services.Reasoning.Memory;

/// <summary>
/// In-memory implementation of <see cref="IReasoningMemoryService"/>.
///
/// Stores entries in a bounded thread-safe list. The list acts as a ring
/// buffer with a configurable maximum size, dropping the oldest entries
/// when the limit is reached.
///
/// Persistence: entries are not currently written to disk. The SQLite
/// persistence layer (Phase 1 roadmap item) will provide optional durable
/// storage when implemented.
///
/// Thread-safety: all mutations and reads are protected by a single
/// ReaderWriterLockSlim for high read / low write concurrency.
/// </summary>
public sealed class ReasoningMemoryService : IReasoningMemoryService, IDisposable
{
    private readonly ReaderWriterLockSlim       _lock    = new();
    private readonly List<ReasoningMemoryEntry> _entries = [];
    private readonly int                        _maxSize;
    private          bool                       _disposed;

    private static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(30);
    private const            int     DefaultMaxSize   = 2000;

    public ReasoningMemoryService(int maxSize = DefaultMaxSize)
    {
        _maxSize = maxSize;
    }

    // ── IReasoningMemoryService ───────────────────────────────────────────────

    /// <inheritdoc />
    public void Record(OperationalInference inference)
    {
        ArgumentNullException.ThrowIfNull(inference);

        // Only record advisory and above — background and informational
        // inferences are too noisy to persist.
        if (inference.Priority < InferencePriority.Advisory) return;

        var entry = new ReasoningMemoryEntry
        {
            InferenceId           = inference.InferenceId,
            Domains               = inference.Domains,
            Priority              = inference.Priority,
            ConfidenceAtInference = inference.Confidence.Level,
            WorkloadContext       = inference.WorkloadContextAtInference,
        };

        _lock.EnterWriteLock();
        try
        {
            _entries.Add(entry);
            if (_entries.Count > _maxSize)
                _entries.RemoveAt(0); // Drop oldest
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <inheritdoc />
    public void UpdateOutcome(Guid inferenceId, bool wasValidated, bool userActed = false)
    {
        _lock.EnterWriteLock();
        try
        {
            var idx = _entries.FindLastIndex(e => e.InferenceId == inferenceId);
            if (idx < 0) return;

            _entries[idx] = _entries[idx] with
            {
                WasValidated       = wasValidated,
                UserActed          = userActed,
                OutcomeObservedAt  = DateTime.UtcNow,
            };
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <inheritdoc />
    public IReadOnlyList<ReasoningMemoryEntry> GetRecent(int maxCount = 100)
    {
        _lock.EnterReadLock();
        try
        {
            return [.. _entries.TakeLast(maxCount).Reverse()];
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <inheritdoc />
    public IReadOnlyList<ReasoningMemoryEntry> GetByDomain(string domain, TimeSpan window)
    {
        var cutoff = DateTime.UtcNow - window;
        _lock.EnterReadLock();
        try
        {
            return [.. _entries
                .Where(e => e.RecordedAt >= cutoff &&
                            e.Domains.Contains(domain, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(e => e.RecordedAt)];
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <inheritdoc />
    public double? GetValidationRate(string domain, int minSamples = 5)
    {
        _lock.EnterReadLock();
        try
        {
            var withOutcome = _entries
                .Where(e => e.Domains.Contains(domain, StringComparer.OrdinalIgnoreCase) &&
                            e.WasValidated.HasValue)
                .ToList();

            if (withOutcome.Count < minSamples) return null;
            return (double)withOutcome.Count(e => e.WasValidated == true) / withOutcome.Count;
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <inheritdoc />
    public int GetRecurrenceCount(string domain, TimeSpan window)
    {
        var cutoff = DateTime.UtcNow - window;
        _lock.EnterReadLock();
        try
        {
            return _entries.Count(e =>
                e.RecordedAt >= cutoff &&
                e.Domains.Contains(domain, StringComparer.OrdinalIgnoreCase));
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <inheritdoc />
    public void Prune(TimeSpan? retentionWindow = null)
    {
        var cutoff = DateTime.UtcNow - (retentionWindow ?? DefaultRetention);
        _lock.EnterWriteLock();
        try
        {
            _entries.RemoveAll(e => e.RecordedAt < cutoff);
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.Dispose();
    }
}
