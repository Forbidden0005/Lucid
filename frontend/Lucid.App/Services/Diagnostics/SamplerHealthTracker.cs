namespace Lucid.Services.Diagnostics;

/// <summary>
/// Tracks per-sampler health within <see cref="WindowsTelemetryService"/>.
///
/// Samplers (CPU, RAM, GPU, Disk, Thermal, Process) are tracked independently.
/// Callers report success/failure after each sample; this tracker maintains
/// rolling statistics without any I/O.
///
/// Design:
///   • Mutable internal state guarded by a single lock (no allocation on hot path).
///   • Average duration uses an exponential moving average (α = 0.2) to avoid
///     unbounded accumulation.
///   • A sampler is considered "unhealthy" after 3 consecutive failures.
///
/// Thread safety: all public methods are safe to call from any thread.
/// </summary>
public sealed class SamplerHealthTracker
{
    // ── Configuration ─────────────────────────────────────────────────────────

    private const int    UnhealthyThreshold = 3;
    private const double EmaAlpha           = 0.2;   // exponential MA smoothing factor

    // ── Internal mutable state ────────────────────────────────────────────────

    private sealed class MutableEntry
    {
        public int            SuccessCount        { get; set; }
        public int            FailureCount        { get; set; }
        public int            ConsecutiveFailures { get; set; }
        public DateTimeOffset LastSuccessAt       { get; set; } = DateTimeOffset.MinValue;
        public DateTimeOffset? LastFailureAt      { get; set; }
        public double         AvgDurationMs       { get; set; }

        public SamplerHealthEntry ToRecord(string name) => new(
            Name:               name,
            SuccessCount:       SuccessCount,
            FailureCount:       FailureCount,
            ConsecutiveFailures: ConsecutiveFailures,
            LastSuccessAt:      LastSuccessAt,
            LastFailureAt:      LastFailureAt,
            AvgDurationMs:      Math.Round(AvgDurationMs, 1));
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly object _lock = new();
    private readonly Dictionary<string, MutableEntry> _entries = new();

    // ── Recording ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Records a successful sampler invocation.
    /// </summary>
    /// <param name="name">Stable sampler name (e.g. "CPU", "RAM", "GPU").</param>
    /// <param name="durationMs">Wall-clock time taken to complete the sample.</param>
    public void RecordSuccess(string name, double durationMs)
    {
        lock (_lock)
        {
            var e = GetOrCreate(name);
            e.SuccessCount++;
            e.ConsecutiveFailures = 0;
            e.LastSuccessAt       = DateTimeOffset.Now;

            // Exponential moving average — avoids unbounded accumulation.
            e.AvgDurationMs = e.SuccessCount == 1
                ? durationMs
                : e.AvgDurationMs * (1 - EmaAlpha) + durationMs * EmaAlpha;
        }
    }

    /// <summary>
    /// Records a sampler failure. Increments consecutive-failure counter.
    /// </summary>
    public void RecordFailure(string name, string? errorMessage = null)
    {
        lock (_lock)
        {
            var e = GetOrCreate(name);
            e.FailureCount++;
            e.ConsecutiveFailures++;
            e.LastFailureAt = DateTimeOffset.Now;
            // Note: errorMessage is not stored here — callers should post to
            // DiagnosticsEventAggregator for detail. This tracker stays cheap.
            _ = errorMessage;
        }
    }

    // ── Observation ───────────────────────────────────────────────────────────

    /// <summary>Returns a health snapshot for the named sampler, or null if never seen.</summary>
    public SamplerHealthEntry? GetHealth(string name)
    {
        lock (_lock)
            return _entries.TryGetValue(name, out var e) ? e.ToRecord(name) : null;
    }

    /// <summary>Returns health snapshots for all tracked samplers.</summary>
    public IReadOnlyList<SamplerHealthEntry> GetAll()
    {
        lock (_lock)
            return [.. _entries.Select(kvp => kvp.Value.ToRecord(kvp.Key))
                               .OrderBy(e => e.Name)];
    }

    /// <summary>
    /// Returns true if any sampler has reached the unhealthy threshold.
    /// Used by <see cref="DegradedModeController"/> to evaluate transitions.
    /// </summary>
    public bool AnyUnhealthy()
    {
        lock (_lock)
            return _entries.Values.Any(e => e.ConsecutiveFailures >= UnhealthyThreshold);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private MutableEntry GetOrCreate(string name)
    {
        if (!_entries.TryGetValue(name, out var e))
        {
            e = new MutableEntry();
            _entries[name] = e;
        }
        return e;
    }
}
