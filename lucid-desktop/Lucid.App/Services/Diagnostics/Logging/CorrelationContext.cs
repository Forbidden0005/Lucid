namespace Lucid.Services.Diagnostics.Logging;

/// <summary>
/// Captures the correlation identifiers that link a log entry to a broader
/// operational context such as a session, workflow, or conversation turn.
///
/// Correlation IDs flow from event bus events (<see cref="Services.Infrastructure.Events.OperationalEvent.CorrelationId"/>)
/// into log entries so multi-step operational sequences can be traced
/// end-to-end through the log file.
///
/// Immutable record — create a new context per logical operation.
/// </summary>
/// <param name="CorrelationId">
/// Top-level correlation ID for this operation or workflow instance.
/// </param>
/// <param name="SessionId">
/// Optional session ID from <see cref="Services.Session.SessionContext"/>.
/// </param>
/// <param name="WorkflowId">
/// Optional workflow instance ID when the log entry is part of a multi-step
/// workflow execution.
/// </param>
/// <param name="ConversationId">
/// Optional companion conversation session identifier.
/// </param>
public sealed record CorrelationContext(
    string  CorrelationId,
    string? SessionId      = null,
    string? WorkflowId     = null,
    string? ConversationId = null)
{
    /// <summary>
    /// Creates a new <see cref="CorrelationContext"/> with a freshly generated
    /// correlation ID and no additional context.
    /// </summary>
    public static CorrelationContext New() =>
        new(Guid.NewGuid().ToString("N")[..12]);

    /// <summary>
    /// Creates a correlation context from an existing event's correlation ID.
    /// Returns a new correlation context if the event has no ID.
    /// </summary>
    public static CorrelationContext FromEvent(string? correlationId) =>
        new(correlationId ?? Guid.NewGuid().ToString("N")[..12]);

    /// <summary>Returns a compact string representation for log embedding.</summary>
    public override string ToString() =>
        WorkflowId is not null
            ? $"{CorrelationId}|wf:{WorkflowId[..Math.Min(8, WorkflowId.Length)]}"
            : CorrelationId;
}
