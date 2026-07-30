using Lucid.Services.Infrastructure.Events;

namespace Lucid.Services.Diagnostics.Governance;

/// <summary>
/// Published on the event bus whenever a trust-relevant governance event occurs.
///
/// Categories:
///   TrustPostureTampered  — consent settings were modified outside the UI
///   ConsentModeChanged    — user changed the consent mode via the UI
///   AutomationModeChanged — user changed the automation mode
///   EndpointRejected      — a non-local LLM/inference endpoint was rejected
///   OperationDenied       — an executor refused to run due to trust rules
///   ProcessIdMismatch     — process identity validation failed before a destructive action
/// </summary>
public sealed record GovernanceAuditEvent(
    string  Category,
    string  Detail,
    string? AffectedValue = null) : OperationalEvent
{
    // Typed factory methods for each category — prevents misspelling category strings.

    public static GovernanceAuditEvent TrustPostureTampered(string detail) =>
        new(Category: "TrustPostureTampered", Detail: detail)
        {
            Source   = "GovernanceAudit",
            Priority = EventPriority.High,
        };

    public static GovernanceAuditEvent ConsentModeChanged(string from, string to) =>
        new(Category: "ConsentModeChanged",
            Detail:   $"Consent mode changed: '{from}' → '{to}'",
            AffectedValue: to)
        {
            Source   = "GovernanceAudit",
            Priority = EventPriority.Normal,
        };

    public static GovernanceAuditEvent AutomationModeChanged(string from, string to) =>
        new(Category: "AutomationModeChanged",
            Detail:   $"Automation mode changed: '{from}' → '{to}'",
            AffectedValue: to)
        {
            Source   = "GovernanceAudit",
            Priority = EventPriority.Normal,
        };

    public static GovernanceAuditEvent EndpointRejected(string url, string reason) =>
        new(Category: "EndpointRejected",
            Detail:   reason,
            AffectedValue: url)
        {
            Source   = "GovernanceAudit",
            Priority = EventPriority.High,
        };

    public static GovernanceAuditEvent OperationDenied(string operationId, string reason) =>
        new(Category: "OperationDenied",
            Detail:   reason,
            AffectedValue: operationId)
        {
            Source   = "GovernanceAudit",
            Priority = EventPriority.Normal,
        };

    public static GovernanceAuditEvent ProcessIdMismatch(int pid, string expected, string actual) =>
        new(Category: "ProcessIdMismatch",
            Detail:   $"PID {pid}: expected '{expected}', found '{actual}' — termination aborted.",
            AffectedValue: pid.ToString())
        {
            Source   = "GovernanceAudit",
            Priority = EventPriority.High,
        };
}
