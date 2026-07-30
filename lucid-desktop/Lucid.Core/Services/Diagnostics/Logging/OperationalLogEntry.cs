namespace Lucid.Services.Diagnostics.Logging;

/// <summary>
/// Immutable structured log entry produced by <see cref="IOperationalLogger"/>.
///
/// Carries the full operational context for a single log event — severity,
/// category, correlation, message, and optional exception detail.
///
/// Entries are written to the file sink via the underlying
/// <see cref="Lucid.Core.Infrastructure.ILucidLogger"/> and can optionally
/// be exported or persisted by the diagnostics subsystem.
///
/// Privacy: never populate <see cref="Message"/> or <see cref="Detail"/>
/// with user-identifiable data (names, file paths, queries, etc.).
/// </summary>
public sealed record OperationalLogEntry
{
    /// <summary>Unique identifier for this log entry.</summary>
    public Guid EntryId { get; init; } = Guid.NewGuid();

    /// <summary>UTC time the entry was created.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>Severity level of this entry.</summary>
    public LogSeverity Severity { get; init; }

    /// <summary>
    /// Category identifies the service or subsystem that produced this entry.
    /// Use the service name (e.g. "Telemetry", "InsightEngine", "Execution").
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Human-readable log message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Optional additional structured detail. Not written to the file log
    /// by default — used for in-memory diagnostic inspection.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>
    /// Optional correlation context linking this entry to a workflow or session.
    /// </summary>
    public CorrelationContext? Correlation { get; init; }

    /// <summary>Optional exception associated with this entry.</summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Formats a compact single-line representation suitable for display
    /// in the Diagnostics page or export.
    /// </summary>
    public override string ToString()
    {
        var correlation = Correlation is not null ? $" [{Correlation}]" : string.Empty;
        var exSuffix    = Exception  is not null ? $" → {Exception.GetType().Name}: {Exception.Message}" : string.Empty;
        return $"[{Severity,-8}] {Timestamp:HH:mm:ss.fff} [{Category}]{correlation} {Message}{exSuffix}";
    }
}
