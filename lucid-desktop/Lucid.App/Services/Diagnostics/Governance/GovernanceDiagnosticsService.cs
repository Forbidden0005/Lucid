using System.Collections.Concurrent;
using Lucid.Services.Infrastructure.Events;

namespace Lucid.Services.Diagnostics.Governance;

/// <summary>
/// Maintains an in-memory governance audit trail and publishes audit events
/// to the event bus.
///
/// Audit entries persist for the lifetime of the session and are visible
/// on the Diagnostics page. They are not persisted to disk — governance
/// events are operational / transient.
///
/// Thread-safe: all state uses concurrent collections.
/// </summary>
public sealed class GovernanceDiagnosticsService
{
    private const int MaxAuditEntries = 200;

    private readonly IEventBus _eventBus;
    private readonly ConcurrentQueue<TrustTransitionRecord> _auditLog = new();

    public GovernanceDiagnosticsService(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    // ── Recording ─────────────────────────────────────────────────────────────

    /// <summary>Records and publishes a trust posture tamper detection.</summary>
    public void RecordTamperDetected(string detail)
    {
        Record(new TrustTransitionRecord(
            TransitionType: "TrustPostureTampered",
            PreviousValue:  "Unknown (tampered)",
            NewValue:       "SafeDefaults",
            OccurredAtUtc:  DateTime.UtcNow,
            Context:        detail));

        _ = _eventBus.PublishAsync(GovernanceAuditEvent.TrustPostureTampered(detail));
    }

    /// <summary>Records and publishes an endpoint rejection event.</summary>
    public void RecordEndpointRejected(string url, string reason)
    {
        Record(new TrustTransitionRecord(
            TransitionType: "EndpointRejected",
            PreviousValue:  url,
            NewValue:       "http://localhost:11434",
            OccurredAtUtc:  DateTime.UtcNow,
            Context:        reason));

        _ = _eventBus.PublishAsync(GovernanceAuditEvent.EndpointRejected(url, reason));
    }

    /// <summary>Records and publishes a consent mode change.</summary>
    public void RecordConsentModeChanged(string from, string to)
    {
        Record(new TrustTransitionRecord(
            TransitionType: "ConsentModeChanged",
            PreviousValue:  from,
            NewValue:       to,
            OccurredAtUtc:  DateTime.UtcNow));

        _ = _eventBus.PublishAsync(GovernanceAuditEvent.ConsentModeChanged(from, to));
    }

    /// <summary>Records and publishes an automation mode change.</summary>
    public void RecordAutomationModeChanged(string from, string to)
    {
        Record(new TrustTransitionRecord(
            TransitionType: "AutomationModeChanged",
            PreviousValue:  from,
            NewValue:       to,
            OccurredAtUtc:  DateTime.UtcNow));

        _ = _eventBus.PublishAsync(GovernanceAuditEvent.AutomationModeChanged(from, to));
    }

    /// <summary>Records and publishes a denied operation.</summary>
    public void RecordOperationDenied(string operationId, string reason)
    {
        Record(new TrustTransitionRecord(
            TransitionType: "OperationDenied",
            PreviousValue:  "Requested",
            NewValue:       "Denied",
            OccurredAtUtc:  DateTime.UtcNow,
            Context:        $"{operationId}: {reason}"));

        _ = _eventBus.PublishAsync(GovernanceAuditEvent.OperationDenied(operationId, reason));
    }

    /// <summary>Records and publishes a process identity mismatch.</summary>
    public void RecordProcessIdMismatch(int pid, string expected, string actual)
    {
        var detail = $"PID {pid}: expected '{expected}', found '{actual}'";
        Record(new TrustTransitionRecord(
            TransitionType: "ProcessIdMismatch",
            PreviousValue:  expected,
            NewValue:       actual,
            OccurredAtUtc:  DateTime.UtcNow,
            Context:        detail));

        _ = _eventBus.PublishAsync(GovernanceAuditEvent.ProcessIdMismatch(pid, expected, actual));
    }

    // ── Observation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current audit log as a read-only snapshot,
    /// most recent first.
    /// </summary>
    public IReadOnlyList<TrustTransitionRecord> GetAuditLog() =>
        _auditLog.Reverse().ToList().AsReadOnly();

    /// <summary>Total number of governance events recorded this session.</summary>
    public int TotalEventCount => _auditLog.Count;

    // ── Private ───────────────────────────────────────────────────────────────

    private void Record(TrustTransitionRecord entry)
    {
        _auditLog.Enqueue(entry);

        // Bound memory — drop oldest entries when over limit
        while (_auditLog.Count > MaxAuditEntries)
            _auditLog.TryDequeue(out _);
    }
}
