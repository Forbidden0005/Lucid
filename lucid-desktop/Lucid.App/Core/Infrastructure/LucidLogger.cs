using System.Text;

namespace Lucid.Core.Infrastructure;

/// <summary>
/// File-backed implementation of <see cref="ILucidLogger"/>.
///
/// Writes structured log entries to:
///   %LocalAppData%\Lucid\logs\lucid-YYYY-MM-DD.log
///
/// Each line is formatted as:
///   [LEVEL] TIMESTAMP [CATEGORY] message
///   [LEVEL] TIMESTAMP [CATEGORY] message → ExceptionType: message
///
/// Thread-safe: all writes are serialized through a SemaphoreSlim so
/// background service threads can log concurrently without corruption.
///
/// Rotation: one file per calendar day. Files older than 7 days are pruned
/// on initialization to prevent unbounded log accumulation.
/// </summary>
public sealed class LucidLogger : ILucidLogger, IDisposable
{
    private readonly string            _logDirectory;
    private readonly SemaphoreSlim     _writeLock = new(1, 1);
    private          string            _currentLogPath = "";
    private          DateOnly          _currentDate;
    private          bool              _disposed;

    private const int MaxLogAgeDays = 7;

    public LucidLogger()
    {
        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Lucid", "logs");

        Directory.CreateDirectory(_logDirectory);
        PruneOldLogs();
        RefreshLogPath(DateOnly.FromDateTime(DateTime.Now));
    }

    // ── ILucidLogger ──────────────────────────────────────────────────────────

    public void Log(LogLevel level, string category, string message, Exception? exception = null)
    {
        if (_disposed) return;

        var now  = DateTime.Now;
        var line = FormatLine(level, now, category, message, exception);

        // Fire-and-forget with best-effort error handling — the logger must
        // never crash the host process.
        _ = WriteAsync(now, line);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task WriteAsync(DateTime timestamp, string line)
    {
        if (_disposed) return;

        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var today = DateOnly.FromDateTime(timestamp);
            if (today != _currentDate)
                RefreshLogPath(today);

            await File.AppendAllTextAsync(_currentLogPath, line + Environment.NewLine)
                      .ConfigureAwait(false);
        }
        catch
        {
            // Swallow — logging must never propagate exceptions to callers.
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void RefreshLogPath(DateOnly date)
    {
        _currentDate    = date;
        _currentLogPath = Path.Combine(_logDirectory,
            $"lucid-{date:yyyy-MM-dd}.log");
    }

    private static string FormatLine(
        LogLevel   level,
        DateTime   timestamp,
        string     category,
        string     message,
        Exception? exception)
    {
        var sb = new StringBuilder();
        sb.Append($"[{LevelTag(level),-8}] ");
        sb.Append($"{timestamp:yyyy-MM-dd HH:mm:ss.fff} ");
        sb.Append($"[{category}] ");
        sb.Append(message);

        if (exception is not null)
        {
            sb.Append($" → {exception.GetType().Name}: {exception.Message}");
            if (exception.InnerException is { } inner)
                sb.Append($" (inner: {inner.Message})");
        }

        return sb.ToString();
    }

    private static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Debug    => "DEBUG",
        LogLevel.Info     => "INFO",
        LogLevel.Warning  => "WARN",
        LogLevel.Error    => "ERROR",
        LogLevel.Critical => "CRITICAL",
        _                 => "LOG",
    };

    private void PruneOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-MaxLogAgeDays);
            foreach (var file in Directory.EnumerateFiles(_logDirectory, "lucid-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
                catch { /* best-effort */ }
            }
        }
        catch { /* best-effort */ }
    }

    public void Dispose()
    {
        _disposed = true;
        _writeLock.Dispose();
    }
}
