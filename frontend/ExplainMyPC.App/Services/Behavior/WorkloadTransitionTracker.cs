namespace ExplainMyPC.Services.Behavior;

/// <summary>
/// Tracks transitions between workload types with debouncing to suppress
/// short-lived fluctuations.
///
/// A transition is committed only when the new workload type has been
/// observed <see cref="MinStableCount"/> consecutive times. At the default
/// 30-second classification interval this equals ~90 seconds of stable signal
/// before a transition is recorded.
///
/// Not thread-safe — must be called from a single classification thread
/// (the telemetry reading thread in WorkloadProfilingService).
/// </summary>
internal sealed class WorkloadTransitionTracker
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>
    /// Consecutive same-workload classifications required before committing.
    /// Prevents noise from momentary classification flips.
    /// </summary>
    private const int MinStableCount = 3;

    /// <summary>Maximum number of historical transitions retained in memory.</summary>
    private const int MaxHistory = 50;

    // ── State ─────────────────────────────────────────────────────────────────

    private WorkloadType   _committed      = WorkloadType.Unknown;
    private DateTimeOffset _committedSince = DateTimeOffset.Now;

    private WorkloadType _pending   = WorkloadType.Unknown;
    private int          _pendingCt = 0;

    private readonly List<WorkloadTransition> _history = [];

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>The currently committed (stable) workload type.</summary>
    public WorkloadType CommittedWorkload => _committed;

    /// <summary>Full transition history, oldest first.</summary>
    public IReadOnlyList<WorkloadTransition> History => _history;

    /// <summary>
    /// Feeds one classification observation.
    ///
    /// Returns a <see cref="WorkloadTransition"/> when a stable transition
    /// is detected for the first time; returns null otherwise.
    /// </summary>
    public WorkloadTransition? Observe(WorkloadClassification classification)
    {
        var observed = classification.PrimaryWorkload;

        if (observed == _committed)
        {
            // Reset the pending streak — we're still in the committed workload
            _pending   = WorkloadType.Unknown;
            _pendingCt = 0;
            return null;
        }

        if (observed == _pending)
        {
            _pendingCt++;
        }
        else
        {
            // New candidate — start a fresh streak
            _pending   = observed;
            _pendingCt = 1;
        }

        if (_pendingCt < MinStableCount) return null;

        // Stable new workload — commit the transition
        var transition = new WorkloadTransition(
            FromWorkload:               _committed,
            ToWorkload:                 observed,
            OccurredAt:                 DateTimeOffset.Now,
            DurationInPreviousWorkload: DateTimeOffset.Now - _committedSince);

        _committed      = observed;
        _committedSince = DateTimeOffset.Now;
        _pending        = WorkloadType.Unknown;
        _pendingCt      = 0;

        _history.Add(transition);
        if (_history.Count > MaxHistory)
            _history.RemoveAt(0);

        return transition;
    }
}
