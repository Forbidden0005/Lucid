namespace ExplainMyPC.Services.Diagnostics;

/// <summary>
/// Tracks per-service health state for all internal ExplainMyPC services.
///
/// Services are registered by name. Each call to <see cref="RecordSuccess"/> or
/// <see cref="RecordFailure"/> updates the corresponding health record with the
/// current timestamp and cumulative counters.
///
/// Design:
///   • Records are immutable value objects (records with 'with' expressions for updates).
///   • Internal state is a mutable dictionary protected by a single lock.
///   • Health records are never deleted — a service that stops reporting simply
///     retains its last known state.
///
/// Thread safety: all public methods are safe to call from any thread.
/// </summary>
public sealed class ServiceHealthMonitor
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>
    /// Number of consecutive failures before a service transitions from
    /// Degraded to Failed.
    /// </summary>
    private const int FailedThreshold = 5;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly object _lock = new();
    private readonly Dictionary<string, ServiceHealthRecord> _records = new();

    // ── Registration ──────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a service by name with Unknown initial status.
    /// Safe to call multiple times — subsequent calls are no-ops.
    /// </summary>
    public void Register(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        lock (_lock)
        {
            if (!_records.ContainsKey(serviceName))
            {
                _records[serviceName] = new ServiceHealthRecord(
                    ServiceName:         serviceName,
                    Status:              ServiceHealthStatus.Unknown,
                    StartedAt:           DateTimeOffset.Now,
                    LastSuccessAt:       DateTimeOffset.MinValue,
                    LastFailureAt:       null,
                    FailureCount:        0,
                    ConsecutiveFailures: 0,
                    RecoveryAttempts:    0,
                    LastFailureMessage:  null);
            }
        }
    }

    // ── Recording ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Marks a successful heartbeat / operation for the named service.
    /// Resets consecutive-failure counter. Transitions status to Healthy.
    /// </summary>
    public void RecordSuccess(string serviceName)
    {
        lock (_lock)
        {
            var rec = GetOrCreate(serviceName);
            _records[serviceName] = rec with
            {
                Status              = ServiceHealthStatus.Healthy,
                LastSuccessAt       = DateTimeOffset.Now,
                ConsecutiveFailures = 0,
            };
        }
    }

    /// <summary>
    /// Marks a failure for the named service.
    /// Increments counters; transitions status to Degraded or Failed based
    /// on consecutive-failure count.
    /// </summary>
    public void RecordFailure(string serviceName, string? message = null)
    {
        lock (_lock)
        {
            var rec = GetOrCreate(serviceName);
            int consecutive = rec.ConsecutiveFailures + 1;
            var newStatus   = consecutive >= FailedThreshold
                ? ServiceHealthStatus.Failed
                : ServiceHealthStatus.Degraded;

            _records[serviceName] = rec with
            {
                Status              = newStatus,
                LastFailureAt       = DateTimeOffset.Now,
                FailureCount        = rec.FailureCount + 1,
                ConsecutiveFailures = consecutive,
                LastFailureMessage  = message,
            };
        }
    }

    /// <summary>
    /// Records a recovery attempt in progress for the named service.
    /// Transitions status to Recovering.
    /// </summary>
    public void RecordRecoveryAttempt(string serviceName)
    {
        lock (_lock)
        {
            var rec = GetOrCreate(serviceName);
            _records[serviceName] = rec with
            {
                Status           = ServiceHealthStatus.Recovering,
                RecoveryAttempts = rec.RecoveryAttempts + 1,
            };
        }
    }

    // ── Observation ───────────────────────────────────────────────────────────

    /// <summary>Returns a snapshot of all registered service health records.</summary>
    public IReadOnlyDictionary<string, ServiceHealthRecord> GetAll()
    {
        lock (_lock)
            return new Dictionary<string, ServiceHealthRecord>(_records);
    }

    /// <summary>Returns the health record for a specific service, or null if unknown.</summary>
    public ServiceHealthRecord? GetHealth(string serviceName)
    {
        lock (_lock)
            return _records.TryGetValue(serviceName, out var rec) ? rec : null;
    }

    /// <summary>
    /// Returns the count of services in Failed or Degraded status.
    /// Used by <see cref="DegradedModeController"/> to evaluate transitions.
    /// </summary>
    public (int DegradedCount, int FailedCount) GetDegradationCounts()
    {
        lock (_lock)
        {
            int degraded = _records.Values.Count(r => r.Status == ServiceHealthStatus.Degraded);
            int failed   = _records.Values.Count(r => r.Status == ServiceHealthStatus.Failed);
            return (degraded, failed);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private ServiceHealthRecord GetOrCreate(string serviceName)
    {
        if (!_records.TryGetValue(serviceName, out var rec))
        {
            rec = new ServiceHealthRecord(
                ServiceName:         serviceName,
                Status:              ServiceHealthStatus.Unknown,
                StartedAt:           DateTimeOffset.Now,
                LastSuccessAt:       DateTimeOffset.MinValue,
                LastFailureAt:       null,
                FailureCount:        0,
                ConsecutiveFailures: 0,
                RecoveryAttempts:    0,
                LastFailureMessage:  null);
            _records[serviceName] = rec;
        }
        return rec;
    }
}
