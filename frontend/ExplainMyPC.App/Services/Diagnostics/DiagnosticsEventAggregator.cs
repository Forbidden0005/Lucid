namespace ExplainMyPC.Services.Diagnostics;

/// <summary>
/// Thread-safe bounded ring-buffer of <see cref="DiagnosticsEvent"/> records.
///
/// Design:
///   • Fixed capacity (<see cref="MaxEvents"/>). When full, the oldest event
///     is evicted to make room — diagnostics never allocates unboundedly.
///   • All writes and reads are protected by a single lock (cheap: no I/O).
///   • Subscribers receive a notification after each append (on the calling
///     thread) so the governance page can update live.
///
/// Thread safety: all public methods are safe to call from any thread.
/// </summary>
public sealed class DiagnosticsEventAggregator
{
    // ── Configuration ─────────────────────────────────────────────────────────

    public const int MaxEvents = 500;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly object                  _lock   = new();
    private readonly List<DiagnosticsEvent>  _events = new(MaxEvents);

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised after each successful append. Fired on the thread that called
    /// <see cref="Append"/> — subscribers must be cheap and non-blocking.
    /// </summary>
    public event EventHandler<DiagnosticsEvent>? EventAppended;

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends a diagnostics event.  If the buffer is at capacity, the oldest
    /// entry is silently evicted.  Never throws.
    /// </summary>
    public void Append(DiagnosticsEvent ev)
    {
        if (ev is null) return;

        lock (_lock)
        {
            if (_events.Count >= MaxEvents)
                _events.RemoveAt(0);
            _events.Add(ev);
        }

        try { EventAppended?.Invoke(this, ev); }
        catch { /* subscriber failures must not propagate */ }
    }

    /// <summary>Convenience overload — constructs and appends in one call.</summary>
    public void Append(
        DiagnosticsEventType type,
        DiagnosticSeverity   severity,
        string               serviceName,
        string               message,
        string?              detail = null)
    {
        Append(new DiagnosticsEvent(type, severity, serviceName, message,
                                    DateTimeOffset.Now, detail));
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all stored events, newest-first.
    /// The returned list is a snapshot — safe to iterate without holding the lock.
    /// </summary>
    public IReadOnlyList<DiagnosticsEvent> GetAll()
    {
        lock (_lock)
            return [.. _events.OrderByDescending(e => e.OccurredAt)];
    }

    /// <summary>Returns the <paramref name="count"/> most recent events.</summary>
    public IReadOnlyList<DiagnosticsEvent> GetRecent(int count)
    {
        if (count <= 0) return [];
        lock (_lock)
            return [.. _events
                .OrderByDescending(e => e.OccurredAt)
                .Take(count)];
    }

    /// <summary>
    /// Returns events with severity at or above <paramref name="minSeverity"/>.
    /// </summary>
    public IReadOnlyList<DiagnosticsEvent> GetBySeverity(DiagnosticSeverity minSeverity)
    {
        lock (_lock)
            return [.. _events
                .Where(e => e.Severity >= minSeverity)
                .OrderByDescending(e => e.OccurredAt)];
    }

    /// <summary>Total number of events currently stored.</summary>
    public int Count { get { lock (_lock) return _events.Count; } }

    /// <summary>Clears all stored events.</summary>
    public void Clear() { lock (_lock) _events.Clear(); }
}
