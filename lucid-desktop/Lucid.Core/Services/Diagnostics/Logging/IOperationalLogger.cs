namespace Lucid.Services.Diagnostics.Logging;

/// <summary>
/// Platform-facing structured logger with correlation context, operational
/// entry capture, and workflow tracking.
///
/// Wraps <see cref="Lucid.Core.Infrastructure.ILucidLogger"/> (the file sink)
/// and adds:
/// - <see cref="CorrelationContext"/> propagation
/// - <see cref="OperationalLogEntry"/> in-memory capture for the Diagnostics page
/// - Convenience scoped-logging helpers for workflows and sessions
///
/// Services should prefer this over raw <see cref="Lucid.Core.Infrastructure.ILucidLogger"/>
/// once they are lifecycle-managed and require correlated tracing.
///
/// Privacy constraint: never log user-identifiable data (file paths, names,
/// query text, etc.). Log identifiers and operational metadata only.
/// </summary>
public interface IOperationalLogger
{
    // ── Core log method ───────────────────────────────────────────────────────

    /// <summary>
    /// Writes a structured log entry with optional correlation context.
    /// </summary>
    void Log(
        LogSeverity        severity,
        string             category,
        string             message,
        CorrelationContext? correlation = null,
        Exception?          exception  = null);

    // ── Convenience methods ───────────────────────────────────────────────────

    void Debug(string category, string message, CorrelationContext? correlation = null)
        => Log(LogSeverity.Debug, category, message, correlation);

    void Info(string category, string message, CorrelationContext? correlation = null)
        => Log(LogSeverity.Info, category, message, correlation);

    void Warning(string category, string message,
        CorrelationContext? correlation = null, Exception? exception = null)
        => Log(LogSeverity.Warning, category, message, correlation, exception);

    void Error(string category, string message,
        CorrelationContext? correlation = null, Exception? exception = null)
        => Log(LogSeverity.Error, category, message, correlation, exception);

    void Critical(string category, string message,
        CorrelationContext? correlation = null, Exception? exception = null)
        => Log(LogSeverity.Critical, category, message, correlation, exception);

    // ── In-memory capture ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns recently captured log entries for the Diagnostics page.
    /// Returns at most <paramref name="maxCount"/> entries in reverse-chronological order.
    /// </summary>
    IReadOnlyList<OperationalLogEntry> GetRecentEntries(int maxCount = 200);

    /// <summary>
    /// Returns recent entries filtered by minimum severity.
    /// </summary>
    IReadOnlyList<OperationalLogEntry> GetRecentEntries(
        LogSeverity minimumSeverity,
        int         maxCount = 200);
}
