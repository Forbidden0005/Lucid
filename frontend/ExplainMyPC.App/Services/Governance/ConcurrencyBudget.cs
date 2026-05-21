namespace ExplainMyPC.Services.Governance;

/// <summary>
/// Thread-safe concurrency budget for the runtime governance layer.
///
/// Tracks in-flight workloads by <see cref="WorkloadCategory"/> and enforces:
///   1. Per-category hard slot limits  (e.g., only one storage scan at a time).
///   2. Global background concurrency ceiling  (set by <see cref="RuntimeMode"/>).
///
/// Foreground workloads bypass the background ceiling — they are always
/// allowed regardless of mode.
///
/// Thread safety: all public methods are protected by a single lock.
/// They are designed to be cheap (no I/O, no allocation in hot path).
/// </summary>
public sealed class ConcurrencyBudget
{
    // ── Per-category hard limits ───────────────────────────────────────────────

    /// <summary>
    /// Maximum simultaneous slots for categories that have one.
    /// Categories not listed here get an implicit limit of int.MaxValue.
    /// </summary>
    private static readonly Dictionary<WorkloadCategory, int> CategoryMaxSlots = new()
    {
        [WorkloadCategory.ActionExecution]     = 1,   // one repair at a time
        [WorkloadCategory.StorageScan]         = 1,   // one tree traversal at a time
        [WorkloadCategory.DuplicateHashing]    = 1,   // SHA-256 is CPU-intensive
        [WorkloadCategory.HistoricalAnalytics] = 1,   // one analytics pass at a time
        [WorkloadCategory.LearningAnalysis]    = 1,   // one learning pass at a time
        [WorkloadCategory.ReplayAnalysis]      = 1,   // one replay session at a time
    };

    // ── Mutable state (all guarded by _lock) ──────────────────────────────────

    private readonly object                         _lock        = new();
    private readonly Dictionary<WorkloadCategory, int> _active  = new();
    private readonly List<ActiveWorkloadEntry>      _activeList  = [];
    private int                                     _maxBackground;
    private int                                     _usedBackground;

    // ── Construction ──────────────────────────────────────────────────────────

    public ConcurrencyBudget(int initialMaxBackground = 3)
    {
        _maxBackground = initialMaxBackground;
    }

    // ── Slot management ───────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to acquire a concurrency slot for the given workload.
    /// Returns <c>false</c> and sets <paramref name="refusalReason"/> when
    /// a slot cannot be granted.
    /// </summary>
    public bool TryAcquire(
        WorkloadCategory category,
        string           workloadName,
        out string?      refusalReason)
    {
        var priority = WorkloadClassifier.Classify(category);

        lock (_lock)
        {
            // ── Per-category limit ─────────────────────────────────────────
            _active.TryGetValue(category, out int current);
            if (CategoryMaxSlots.TryGetValue(category, out int max) && current >= max)
            {
                refusalReason = $"{WorkloadClassifier.GetDisplayName(category)} " +
                                $"already has {current}/{max} slot(s) in use.";
                return false;
            }

            // ── Background ceiling ─────────────────────────────────────────
            if (priority == WorkloadPriority.Background
                && _usedBackground >= _maxBackground)
            {
                refusalReason = $"Background concurrency limit ({_maxBackground}) reached.";
                return false;
            }

            if (priority == WorkloadPriority.IdleOnly
                && _maxBackground == 0)
            {
                refusalReason = "Idle-only work is paused — background slot budget is zero.";
                return false;
            }

            // ── Grant slot ────────────────────────────────────────────────
            _active[category] = current + 1;

            if (priority >= WorkloadPriority.Background)
                _usedBackground++;

            _activeList.Add(new ActiveWorkloadEntry(
                workloadName, category, priority, DateTimeOffset.Now));

            refusalReason = null;
            return true;
        }
    }

    /// <summary>
    /// Releases a previously acquired slot. Safe to call if the slot was
    /// never acquired (no-op).
    /// </summary>
    public void Release(WorkloadCategory category, string workloadName)
    {
        var priority = WorkloadClassifier.Classify(category);

        lock (_lock)
        {
            if (_active.TryGetValue(category, out int current) && current > 0)
                _active[category] = current - 1;

            if (priority >= WorkloadPriority.Background && _usedBackground > 0)
                _usedBackground--;

            // Remove the first matching active entry
            int idx = _activeList.FindIndex(
                e => e.Category == category && e.Name == workloadName);
            if (idx >= 0)
                _activeList.RemoveAt(idx);
        }
    }

    /// <summary>
    /// Updates the maximum concurrent background slots.
    /// Called by <see cref="RuntimeGovernanceService"/> when the mode changes.
    /// </summary>
    public void UpdateMaxBackground(int newMax)
    {
        lock (_lock) { _maxBackground = newMax; }
    }

    // ── Observation ───────────────────────────────────────────────────────────

    /// <summary>Returns a snapshot of the current active workload list (copy).</summary>
    public IReadOnlyList<ActiveWorkloadEntry> GetActiveWorkloads()
    {
        lock (_lock) return _activeList.ToList();
    }

    /// <summary>
    /// Returns a point-in-time snapshot of the full resource budget.
    /// </summary>
    public ResourceBudgetSnapshot GetSnapshot(
        RuntimeMode      mode,
        ThrottlingReason reasons)
    {
        lock (_lock)
        {
            return new ResourceBudgetSnapshot(
                Mode:                    mode,
                Reasons:                 reasons,
                ActiveWorkloadCount:     _activeList.Count,
                MaxConcurrentBackground: _maxBackground,
                UsedBackgroundSlots:     _usedBackground,
                TelemetryInterval:       AdaptiveSchedulingPolicy.GetTelemetryInterval(mode),
                ProcessSampleInterval:   AdaptiveSchedulingPolicy.GetProcessSampleInterval(mode),
                SnapshotAt:              DateTimeOffset.Now);
        }
    }

    /// <summary>
    /// Returns the count of active slots for a specific category.
    /// Zero if no slots are in use.
    /// </summary>
    public int GetActiveCount(WorkloadCategory category)
    {
        lock (_lock)
        {
            _active.TryGetValue(category, out int c);
            return c;
        }
    }
}
