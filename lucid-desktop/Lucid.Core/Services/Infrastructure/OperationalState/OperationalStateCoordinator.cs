namespace Lucid.Services.Infrastructure.OperationalState;

/// <summary>
/// Production implementation of <see cref="IOperationalStateCoordinator"/>.
///
/// All state mutations are serialized through an interlocked snapshot swap so
/// consumers always see a consistent <see cref="Current"/> value without
/// needing to acquire a lock on the read path.
///
/// Write path: lock → mutate internal mutable fields → build new immutable
/// snapshot → Interlocked.Exchange → raise event.
///
/// Read path: single field read (no lock needed — reference is always consistent).
/// </summary>
public sealed class OperationalStateCoordinator : IOperationalStateCoordinator
{
    private readonly object _writeLock = new();

    // Mutable backing fields — only mutated under _writeLock.
    private OperationalMode    _mode              = OperationalMode.Normal;
    private RuntimeCondition   _conditions        = RuntimeCondition.None;
    private double             _cpuPercent;
    private double             _ramPercent;
    // These fields are set by subsystem reporters wired in future updates.
#pragma warning disable CS0649
    private bool               _batteryAwareMode;
    private bool               _simulationRunning;
    private bool               _conversationActive;
    private bool               _automationActive;
#pragma warning restore CS0649
    private string?            _pendingConsentDesc;
    private string?            _pendingConsentId;
    private readonly HashSet<string> _activeWorkflows = [];

    // Health summary — updated by external caller (the composition root or LifecycleCoordinator).
    private int _degradedServiceCount;
    private int _failedServiceCount;

    // Latest published snapshot — written atomically, read lock-free.
    private volatile OperationalStateSnapshot _current = OperationalStateSnapshot.Empty;

    // ── IOperationalStateCoordinator ─────────────────────────────────────────

    /// <inheritdoc />
    public OperationalStateSnapshot Current => _current;

    /// <inheritdoc />
    public event Action<OperationalStateSnapshot>? StateChanged;

    /// <inheritdoc />
    public void SetCondition(RuntimeCondition condition)
    {
        lock (_writeLock)
        {
            if ((_conditions & condition) == condition) return; // already set
            _conditions |= condition;
        }
        PublishSnapshot();
    }

    /// <inheritdoc />
    public void ClearCondition(RuntimeCondition condition)
    {
        lock (_writeLock)
        {
            if ((_conditions & condition) == RuntimeCondition.None) return; // already clear
            _conditions &= ~condition;
        }
        PublishSnapshot();
    }

    /// <inheritdoc />
    public void SetMode(OperationalMode mode)
    {
        lock (_writeLock)
        {
            if (_mode == mode) return;
            _mode = mode;
        }
        PublishSnapshot();
    }

    /// <inheritdoc />
    public void UpdateResourcePressure(double cpuPercent, double ramPercent)
    {
        bool changed;
        lock (_writeLock)
        {
            // Only publish when values change meaningfully (>1% delta) to avoid
            // flooding StateChanged listeners on every telemetry tick.
            changed = Math.Abs(_cpuPercent - cpuPercent) > 1.0 ||
                      Math.Abs(_ramPercent - ramPercent) > 1.0;

            _cpuPercent = cpuPercent;
            _ramPercent = ramPercent;

            // Mirror into conditions flags for convenience.
            if (cpuPercent > 85) _conditions |= RuntimeCondition.HighCpuPressure;
            else                  _conditions &= ~RuntimeCondition.HighCpuPressure;

            if (ramPercent > 85) _conditions |= RuntimeCondition.HighMemoryPressure;
            else                  _conditions &= ~RuntimeCondition.HighMemoryPressure;
        }

        if (changed) PublishSnapshot();
    }

    /// <inheritdoc />
    public void WorkflowStarted(string workflowId)
    {
        ArgumentException.ThrowIfNullOrEmpty(workflowId);
        lock (_writeLock)
        {
            _activeWorkflows.Add(workflowId);
            _conditions |= RuntimeCondition.WorkflowActive;
        }
        PublishSnapshot();
    }

    /// <inheritdoc />
    public void WorkflowCompleted(string workflowId)
    {
        ArgumentException.ThrowIfNullOrEmpty(workflowId);
        lock (_writeLock)
        {
            _activeWorkflows.Remove(workflowId);
            if (_activeWorkflows.Count == 0)
                _conditions &= ~RuntimeCondition.WorkflowActive;
        }
        PublishSnapshot();
    }

    /// <inheritdoc />
    public void ConsentRequested(string operationId, string description)
    {
        lock (_writeLock)
        {
            _pendingConsentId   = operationId;
            _pendingConsentDesc = description;
            _conditions |= RuntimeCondition.ConsentPending;
        }
        PublishSnapshot();
    }

    /// <inheritdoc />
    public void ConsentResolved(string operationId)
    {
        lock (_writeLock)
        {
            if (_pendingConsentId != operationId) return;
            _pendingConsentId   = null;
            _pendingConsentDesc = null;
            _conditions &= ~RuntimeCondition.ConsentPending;
        }
        PublishSnapshot();
    }

    /// <summary>
    /// Updates the health summary counters. Called by the LifecycleCoordinator
    /// or health polling loop.
    /// </summary>
    public void UpdateServiceHealth(int degradedCount, int failedCount)
    {
        bool changed;
        lock (_writeLock)
        {
            changed = _degradedServiceCount != degradedCount ||
                      _failedServiceCount   != failedCount;

            _degradedServiceCount = degradedCount;
            _failedServiceCount   = failedCount;

            if (degradedCount > 0 || failedCount > 0)
                _conditions |= RuntimeCondition.ServiceDegradation;
            else
                _conditions &= ~RuntimeCondition.ServiceDegradation;
        }
        if (changed) PublishSnapshot();
    }

    // ── Snapshot construction ─────────────────────────────────────────────────

    private void PublishSnapshot()
    {
        OperationalStateSnapshot snapshot;

        lock (_writeLock)
        {
            snapshot = new OperationalStateSnapshot
            {
                Mode                    = _mode,
                ActiveConditions        = _conditions,
                AttentionLevel          = ComputeAttentionLevel(_conditions, _failedServiceCount),
                ActiveWorkflowCount     = _activeWorkflows.Count,
                ActiveWorkflowIds       = [.. _activeWorkflows],
                ConsentPending          = _pendingConsentId is not null,
                PendingConsentDescription = _pendingConsentDesc,
                DegradedServiceCount    = _degradedServiceCount,
                FailedServiceCount      = _failedServiceCount,
                CpuPercent              = _cpuPercent,
                RamPercent              = _ramPercent,
                BatteryAwareMode        = _batteryAwareMode,
                SimulationRunning       = _simulationRunning,
                ConversationActive      = _conversationActive,
                AutomationActive        = _automationActive,
            };
        }

        _current = snapshot;

        try { StateChanged?.Invoke(snapshot); }
        catch { /* StateChanged listeners must not crash the coordinator */ }
    }

    private static SystemAttentionLevel ComputeAttentionLevel(
        RuntimeCondition conditions,
        int              failedCount)
    {
        if (failedCount > 0)
            return SystemAttentionLevel.Urgent;

        if ((conditions & (RuntimeCondition.HighCpuPressure | RuntimeCondition.HighMemoryPressure |
                           RuntimeCondition.ThermalStress   | RuntimeCondition.LowDiskSpace)) != 0)
            return SystemAttentionLevel.ActionRequired;

        if ((conditions & (RuntimeCondition.ServiceDegradation | RuntimeCondition.ConsentPending)) != 0)
            return SystemAttentionLevel.Advisory;

        return SystemAttentionLevel.Normal;
    }
}
