using System.Collections.Concurrent;
using System.Threading;

namespace Lucid.Services.Intelligence.Patterns;

/// <summary>
/// Lightweight in-memory recurrence counter for operational pattern keys.
///
/// Tracks how many times each pattern key has been observed, when it was first
/// and last seen, and what workload contexts it's associated with.
///
/// This is the raw counting layer — <see cref="PatternIntelligenceEngine"/>
/// builds higher-level <see cref="OperationalPattern"/> records on top of it.
///
/// Thread-safe: ConcurrentDictionary + Interlocked operations.
/// In-memory only — recurrence data is session-lifetime (not persisted).
/// </summary>
public sealed class RecurrenceTracker
{
    private const int MaxTrackedPatterns = 500;

    private readonly ConcurrentDictionary<string, RecurrenceEntry> _entries = new();

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Records one occurrence of the given pattern key.
    /// Creates a new entry on first observation.
    /// </summary>
    public void Record(string patternKey, string? workloadContext = null)
    {
        var now = DateTime.UtcNow;

        _entries.AddOrUpdate(
            patternKey,
            addValue: new RecurrenceEntry(
                PatternKey:       patternKey,
                Count:            1,
                FirstSeenAt:      now,
                LastSeenAt:       now,
                WorkloadContexts: workloadContext is not null
                    ? new[] { workloadContext }
                    : []),
            updateValueFactory: (_, existing) => existing with
            {
                Count        = existing.Count + 1,
                LastSeenAt   = now,
                WorkloadContexts = workloadContext is not null
                    ? [.. existing.WorkloadContexts, workloadContext]
                    : existing.WorkloadContexts,
            });

        // Prune if over capacity.
        if (_entries.Count > MaxTrackedPatterns)
            Prune();
    }

    /// <summary>Returns the recurrence entry for the given pattern key, or null.</summary>
    public RecurrenceEntry? Get(string patternKey) =>
        _entries.TryGetValue(patternKey, out var entry) ? entry : null;

    /// <summary>Returns the occurrence count for a pattern key (0 if never seen).</summary>
    public int GetCount(string patternKey) => Get(patternKey)?.Count ?? 0;

    /// <summary>Returns the N most-frequently-occurring patterns, highest first.</summary>
    public IReadOnlyList<RecurrenceEntry> GetTopPatterns(int n = 10) =>
        _entries.Values
                .OrderByDescending(e => e.Count)
                .Take(n)
                .ToList()
                .AsReadOnly();

    /// <summary>Returns all tracked patterns with count ≥ <paramref name="minOccurrences"/>.</summary>
    public IReadOnlyList<RecurrenceEntry> GetEstablished(int minOccurrences = 3) =>
        _entries.Values
                .Where(e => e.Count >= minOccurrences)
                .OrderByDescending(e => e.Count)
                .ToList()
                .AsReadOnly();

    /// <summary>Total number of distinct pattern keys tracked.</summary>
    public int TrackedCount => _entries.Count;

    /// <summary>Clears all tracked data (call on session reset).</summary>
    public void Clear() => _entries.Clear();

    // ── Private ───────────────────────────────────────────────────────────────

    /// <summary>Removes the least-recently-seen entries to stay within capacity.</summary>
    private void Prune()
    {
        var toRemove = _entries.Values
            .OrderBy(e => e.LastSeenAt)
            .Take(_entries.Count - MaxTrackedPatterns + 50)
            .Select(e => e.PatternKey)
            .ToList();

        foreach (var key in toRemove)
            _entries.TryRemove(key, out _);
    }
}

/// <summary>
/// Immutable snapshot of the recurrence state for one pattern key.
/// </summary>
public sealed record RecurrenceEntry(
    string                   PatternKey,
    int                      Count,
    DateTime                 FirstSeenAt,
    DateTime                 LastSeenAt,
    IReadOnlyList<string>    WorkloadContexts)
{
    /// <summary>The most-frequently-associated workload context, or null.</summary>
    public string? DominantWorkloadContext =>
        WorkloadContexts.GroupBy(w => w)
                        .OrderByDescending(g => g.Count())
                        .FirstOrDefault()?.Key;

    /// <summary>Age of this pattern tracking entry.</summary>
    public TimeSpan Age => LastSeenAt - FirstSeenAt;
}
