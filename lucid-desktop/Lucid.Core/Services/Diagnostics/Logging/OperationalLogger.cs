using Lucid.Core.Infrastructure;

namespace Lucid.Services.Diagnostics.Logging;

/// <summary>
/// Production implementation of <see cref="IOperationalLogger"/>.
///
/// Delegates all file writes to the injected <see cref="ILucidLogger"/>
/// (the rotating file sink). Additionally captures entries in a bounded
/// in-memory ring buffer so the Diagnostics page can display recent logs
/// without reading from disk.
///
/// Thread-safe: all operations are safe for concurrent callers.
/// </summary>
public sealed class OperationalLogger : IOperationalLogger, IDisposable
{
    private readonly ILucidLogger             _sink;
    private readonly LinkedList<OperationalLogEntry> _buffer = new();
    private readonly object                   _bufferLock = new();
    private readonly int                      _maxBufferSize;
    private          bool                     _disposed;

    private const int DefaultBufferSize = 500;

    /// <param name="sink">
    /// File-backed log sink. Typically <see cref="LucidLogger"/> created by
    /// the composition root.
    /// </param>
    /// <param name="maxBufferSize">
    /// Maximum number of entries retained in the in-memory ring buffer.
    /// Oldest entries are evicted when the buffer is full.
    /// </param>
    public OperationalLogger(
        ILucidLogger sink,
        int          maxBufferSize = DefaultBufferSize)
    {
        _sink          = sink;
        _maxBufferSize = maxBufferSize;
    }

    // ── IOperationalLogger ────────────────────────────────────────────────────

    /// <inheritdoc />
    public void Log(
        LogSeverity         severity,
        string              category,
        string              message,
        CorrelationContext? correlation = null,
        Exception?          exception  = null)
    {
        if (_disposed) return;

        // Write to file sink.
        var coreLevel = MapSeverity(severity);
        var fileMessage = correlation is not null
            ? $"[{correlation}] {message}"
            : message;
        _sink.Log(coreLevel, category, fileMessage, exception);

        // Capture in memory.
        var entry = new OperationalLogEntry
        {
            Severity    = severity,
            Category    = category,
            Message     = message,
            Correlation = correlation,
            Exception   = exception,
        };

        lock (_bufferLock)
        {
            _buffer.AddFirst(entry); // newest first
            while (_buffer.Count > _maxBufferSize)
                _buffer.RemoveLast();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<OperationalLogEntry> GetRecentEntries(int maxCount = 200)
    {
        lock (_bufferLock)
        {
            return [.. _buffer.Take(maxCount)];
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<OperationalLogEntry> GetRecentEntries(
        LogSeverity minimumSeverity,
        int         maxCount = 200)
    {
        lock (_bufferLock)
        {
            return [.. _buffer
                .Where(e => e.Severity >= minimumSeverity)
                .Take(maxCount)];
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static LogLevel MapSeverity(LogSeverity severity) => severity switch
    {
        LogSeverity.Debug    => LogLevel.Debug,
        LogSeverity.Info     => LogLevel.Info,
        LogSeverity.Warning  => LogLevel.Warning,
        LogSeverity.Error    => LogLevel.Error,
        LogSeverity.Critical => LogLevel.Critical,
        _                    => LogLevel.Info,
    };

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_bufferLock) { _buffer.Clear(); }
    }
}
