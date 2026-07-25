namespace Lucid.Core.Infrastructure;

/// <summary>
/// Severity level for a log entry.
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Critical,
}

/// <summary>
/// Structured logger for the Lucid platform.
///
/// Writes timestamped entries to %LocalAppData%\Lucid\logs\lucid.log.
/// Each entry includes the severity level, category (service/component name),
/// message, and optional exception details.
///
/// Use category names that mirror the service namespace — e.g. "Startup",
/// "Intelligence", "Execution" — so log entries are easy to filter.
///
/// Usage:
///   _logger.Log(LogLevel.Warning, "Startup", "Stale registry key skipped: Teams");
///   _logger.Log(LogLevel.Error,   "Execution", "Executor failed", ex);
/// </summary>
public interface ILucidLogger
{
    void Log(LogLevel level, string category, string message, Exception? exception = null);

    // ── Convenience helpers ────────────────────────────────────────────────

    void Debug(string category, string message)
        => Log(LogLevel.Debug, category, message);

    void Info(string category, string message)
        => Log(LogLevel.Info, category, message);

    void Warning(string category, string message, Exception? exception = null)
        => Log(LogLevel.Warning, category, message, exception);

    void Error(string category, string message, Exception? exception = null)
        => Log(LogLevel.Error, category, message, exception);

    void Critical(string category, string message, Exception? exception = null)
        => Log(LogLevel.Critical, category, message, exception);
}
