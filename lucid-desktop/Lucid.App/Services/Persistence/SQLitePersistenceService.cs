using System.Collections.Concurrent;
using Lucid.Core.Infrastructure;
using Microsoft.Data.Sqlite;
using Lucid.Services.Persistence.Reliability;

namespace Lucid.Services.Persistence;

/// <summary>
/// SQLite connection manager, schema owner, and batched write queue for all
/// Lucid historical persistence.
///
/// Design:
///   • One connection for the lifetime of the application (WAL mode).
///   • All reads and writes serialised through a single SemaphoreSlim.
///   • Write queue: callers enqueue write actions (parameterised SQL); a timer
///     drains them every <see cref="FlushIntervalSeconds"/> in one transaction.
///   • Direct write path: time-sensitive writes (insight onset, outcomes) call
///     ExecuteDirectAsync which bypasses the queue and acquires the lock immediately.
///   • Corruption recovery: if the database cannot be opened, the file is renamed
///     to a backup and a fresh database is created.
///
/// Database location:
///   %LOCALAPPDATA%\Lucid\Data\explainmypc.db
///
/// Threading:
///   All public methods are safe to call from any thread.
///   Callbacks inside EnqueueWrite run on the thread-pool drain thread — do NOT
///   capture UI-thread-only objects.
/// </summary>
public sealed class SQLitePersistenceService : IDisposable
{
    // ── Configuration ─────────────────────────────────────────────────────────

    public  const int CurrentSchemaVersion   = 1;
    private const int FlushIntervalSeconds   = 30;
    private const int MaxQueueDepth          = 2000;
    private const int MaxFlushBatchSize      = 500;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly string         _dbPath;
    private readonly ILucidLogger?  _logger;
    private readonly bool           _startFlushTimer;
    private          SqliteConnection? _connection;
    private readonly SemaphoreSlim  _lock   = new(1, 1);
    private readonly ConcurrentQueue<Action<SqliteConnection>> _writeQueue = new();
    private          Timer?         _flushTimer;
    private          bool           _disposed;
    private          bool           _healthy;

    // ── Reliability metrics ───────────────────────────────────────────────────

    /// <summary>
    /// Live write-queue health metrics exposed to the diagnostics layer.
    /// Incremented inline — no lock required.
    /// </summary>
    public WriteQueueMetrics QueueMetrics { get; } = new();

    /// <summary>
    /// Optional callback invoked when a write is dropped due to queue overflow.
    /// Arguments: (currentQueueDepth, maxQueueDepth).
    /// Set by AppServices after construction to wire in PersistenceHealthMonitor.
    /// </summary>
    public Action<int, int>? OnWriteDropped { get; set; }

    // ── Construction ──────────────────────────────────────────────────────────

    public SQLitePersistenceService(ILucidLogger? logger = null)
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Lucid", "Data", "explainmypc.db"),
            logger,
            startFlushTimer: true)
    {
    }

    internal SQLitePersistenceService(
        string dbPath,
        ILucidLogger? logger = null,
        bool startFlushTimer = true)
    {
        _logger = logger;
        _startFlushTimer = startFlushTimer;
        _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
        var dir = Path.GetDirectoryName(_dbPath);
        if (string.IsNullOrWhiteSpace(dir))
            throw new ArgumentException("Database path must include a parent directory.", nameof(dbPath));

        try { Directory.CreateDirectory(dir); }
        catch (Exception ex)
        {
            _logger?.Warning("Persistence", $"Could not create data directory '{dir}' — writes will fail", ex);
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the database, applies any pending migrations, and starts the
    /// background flush timer.  Safe to call once from AppServices.Initialize.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _connection = OpenConnectionCore(_dbPath);
            ApplyPragmas(_connection);
            await ApplyMigrationsAsync(_connection).ConfigureAwait(false);
            _healthy = true;
        }
        catch (Exception ex)
        {
            // Corruption recovery — rename and recreate
            TryBackupAndDelete(_dbPath);
            try
            {
                _connection = OpenConnectionCore(_dbPath);
                ApplyPragmas(_connection);
                await ApplyMigrationsAsync(_connection).ConfigureAwait(false);
                _healthy = true;
            }
            catch (Exception recoveryEx)
            {
                // Can't open at all — disable persistence without crashing
                _healthy = false;
                _logger?.Error("Persistence",
                    $"SQLite database '{_dbPath}' is unrecoverable after corruption recovery attempt — persistence disabled. " +
                    $"Original: {ex.Message}",
                    recoveryEx);
            }
        }
        finally
        {
            _lock.Release();
        }

        // Start flush timer only when healthy
        if (_healthy && _startFlushTimer)
        {
            _flushTimer = new Timer(
                async _ => await FlushQueueAsync().ConfigureAwait(false),
                null,
                TimeSpan.FromSeconds(FlushIntervalSeconds),
                TimeSpan.FromSeconds(FlushIntervalSeconds));
        }
    }

    /// <summary>True when the database opened and migrated successfully.</summary>
    public bool IsHealthy => _healthy;

    /// <summary>Full path to the database file.</summary>
    public string DatabasePath => _dbPath;

    /// <summary>
    /// Number of write actions currently pending in the batch queue.
    /// Used by the diagnostics layer to detect write stalls.
    /// </summary>
    public int PendingWriteCount => _writeQueue.Count;

    // ── Write API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Enqueues a write action to be executed in the next batch flush.
    /// Use for non-critical, high-volume writes (telemetry samples, timeline events).
    /// Silently drops the write when the queue is at capacity to bound memory use.
    /// </summary>
    public void EnqueueWrite(Action<SqliteConnection> writeAction)
    {
        if (!_healthy || _disposed) return;

        var depth = _writeQueue.Count;
        if (depth >= MaxQueueDepth)
        {
            // Back-pressure: drop the write but make it visible.
            QueueMetrics.RecordDrop();
            OnWriteDropped?.Invoke(depth, MaxQueueDepth);
            return;
        }

        QueueMetrics.RecordEnqueue();
        _writeQueue.Enqueue(writeAction);
    }

    /// <summary>
    /// Executes a write immediately, acquiring the lock and bypassing the queue.
    /// Use for time-sensitive, low-volume writes (insight onset/resolution, outcomes).
    /// </summary>
    public async Task ExecuteDirectAsync(Action<SqliteConnection> writeAction)
    {
        if (!_healthy || _disposed || _connection is null) return;

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            writeAction(_connection);
        }
        catch (Exception ex)
        {
            _logger?.Warning("Persistence", "ExecuteDirectAsync write failed", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Read API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a query on the connection, returning the mapped result.
    /// Returns default(T) on any failure.
    /// </summary>
    public async Task<T?> QueryAsync<T>(Func<SqliteConnection, T> queryFunc)
    {
        if (!_healthy || _disposed || _connection is null) return default;

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            return queryFunc(_connection);
        }
        catch (Exception ex)
        {
            _logger?.Warning("Persistence", "QueryAsync failed — returning default", ex);
            return default;
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Flush ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Drains the write queue in a single BEGIN TRANSACTION / COMMIT block.
    /// Called by the flush timer and also on Dispose().
    /// </summary>
    public async Task FlushQueueAsync()
    {
        if (!_healthy || _disposed || _connection is null || _writeQueue.IsEmpty)
            return;

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var tx = _connection.BeginTransaction();
            int count = 0;

            while (count < MaxFlushBatchSize &&
                   _writeQueue.TryDequeue(out var action))
            {
                try
                {
                    action(_connection);
                }
                catch (Exception ex)
                {
                    _logger?.Debug("Persistence", $"Queued write skipped in batch flush: {ex.Message}");
                }
                count++;
            }

            tx.Commit();
            QueueMetrics.RecordFlush();
        }
        catch (Exception ex)
        {
            _logger?.Error("Persistence", "FlushQueueAsync transaction commit failed — writes in this batch are lost", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _flushTimer?.Dispose();

        // Final flush — sync (Dispose must not async-block)
        var pendingCount = _writeQueue.Count;
        try
        {
            if (!_lock.Wait(TimeSpan.FromSeconds(5)))
            {
                _logger?.Warning("Persistence",
                    $"Final flush timed out waiting for lock — {pendingCount} pending write(s) may be lost");
            }
            else
            {
                try
                {
                    if (_healthy && _connection is not null && !_writeQueue.IsEmpty)
                    {
                        using var tx = _connection.BeginTransaction();
                        while (_writeQueue.TryDequeue(out var action))
                        {
                            try { action(_connection); }
                            catch (Exception ex)
                            {
                                _logger?.Debug("Persistence", $"Write skipped in final flush: {ex.Message}");
                            }
                        }
                        tx.Commit();
                        _logger?.Debug("Persistence", $"Final flush committed {pendingCount} pending write(s)");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Error("Persistence", "Final flush commit failed — pending writes lost", ex);
                }
                finally
                {
                    _lock.Release();
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning("Persistence", "Exception during final flush", ex);
        }

        _connection?.Dispose();
        _lock.Dispose();
    }

    // ── Schema / migrations ────────────────────────────────────────────────────

    private static SqliteConnection OpenConnectionCore(string path)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode       = SqliteOpenMode.ReadWriteCreate,
            Cache      = SqliteCacheMode.Private,
        }.ToString();

        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }

    private static void ApplyPragmas(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous  = NORMAL;
            PRAGMA busy_timeout = 5000;
            PRAGMA foreign_keys = ON;
            PRAGMA cache_size   = -4096;
            """;
        cmd.ExecuteNonQuery();
    }

    private static async Task ApplyMigrationsAsync(SqliteConnection conn)
    {
        // Read current schema version (0 = brand new database)
        int currentVersion = 0;
        using (var vCmd = conn.CreateCommand())
        {
            vCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS db_metadata (
                    key   TEXT PRIMARY KEY NOT NULL,
                    value TEXT NOT NULL
                );
                """;
            vCmd.ExecuteNonQuery();

            vCmd.CommandText = "SELECT value FROM db_metadata WHERE key = 'schema_version';";
            var raw = vCmd.ExecuteScalar() as string;
            if (raw is not null) int.TryParse(raw, out currentVersion);
        }

        if (currentVersion < 1)
            await ApplyMigration1Async(conn).ConfigureAwait(false);

        // Future migrations: if (currentVersion < 2) await ApplyMigration2Async(conn);
    }

    private static Task ApplyMigration1Async(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            -- Telemetry samples (downsampled history)
            CREATE TABLE IF NOT EXISTS telemetry_samples (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                sampled_at   INTEGER NOT NULL,
                resolution   INTEGER NOT NULL DEFAULT 0,
                cpu_pct      REAL    NOT NULL,
                ram_pct      REAL    NOT NULL,
                gpu_pct      REAL,
                disk_pct     REAL    NOT NULL,
                temp_c       REAL,
                sample_count INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS idx_tel_at  ON telemetry_samples (sampled_at);
            CREATE INDEX IF NOT EXISTS idx_tel_res ON telemetry_samples (resolution, sampled_at);

            -- Timeline events
            CREATE TABLE IF NOT EXISTS timeline_events (
                event_id              TEXT PRIMARY KEY NOT NULL,
                event_type            INTEGER NOT NULL,
                severity              INTEGER NOT NULL,
                title                 TEXT    NOT NULL,
                detail                TEXT,
                occurred_at           INTEGER NOT NULL,
                related_insight_id    TEXT,
                attributed_processes  TEXT,
                action_id             TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_evt_at   ON timeline_events (occurred_at);
            CREATE INDEX IF NOT EXISTS idx_evt_type ON timeline_events (event_type);

            -- Insight onset / resolution history
            CREATE TABLE IF NOT EXISTS insight_history (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                insight_id       TEXT    NOT NULL,
                title            TEXT    NOT NULL,
                severity         INTEGER NOT NULL,
                onset_at         INTEGER NOT NULL,
                resolved_at      INTEGER,
                duration_seconds INTEGER
            );
            CREATE INDEX IF NOT EXISTS idx_ins_id    ON insight_history (insight_id);
            CREATE INDEX IF NOT EXISTS idx_ins_onset ON insight_history (onset_at);

            -- Recommendation outcomes (replaces Learning JSON file)
            CREATE TABLE IF NOT EXISTS recommendation_outcomes (
                operation_id       TEXT    PRIMARY KEY NOT NULL,
                action_key         TEXT    NOT NULL,
                action_title       TEXT    NOT NULL,
                executed_at        INTEGER NOT NULL,
                analyzed_at        INTEGER NOT NULL,
                outcome            TEXT    NOT NULL,
                confidence_pct     INTEGER NOT NULL DEFAULT 0,
                cpu_before         REAL    NOT NULL DEFAULT 0,
                cpu_after          REAL    NOT NULL DEFAULT 0,
                ram_before         REAL    NOT NULL DEFAULT 0,
                ram_after          REAL    NOT NULL DEFAULT 0,
                disk_before        REAL    NOT NULL DEFAULT 0,
                disk_after         REAL    NOT NULL DEFAULT 0,
                insights_before    INTEGER NOT NULL DEFAULT 0,
                insights_after     INTEGER NOT NULL DEFAULT 0,
                insights_resolved  INTEGER NOT NULL DEFAULT 0,
                stab_minutes       INTEGER NOT NULL DEFAULT 0,
                narrative          TEXT    NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS idx_out_key  ON recommendation_outcomes (action_key);
            CREATE INDEX IF NOT EXISTS idx_out_exec ON recommendation_outcomes (executed_at);
            """;
        cmd.ExecuteNonQuery();

        // Record migration
        using var uCmd = conn.CreateCommand();
        uCmd.CommandText = """
            INSERT INTO db_metadata (key, value) VALUES ('schema_version', '1')
            ON CONFLICT(key) DO UPDATE SET value = '1';
            INSERT INTO db_metadata (key, value) VALUES ('created_at', @ts)
            ON CONFLICT(key) DO NOTHING;
            """;
        uCmd.Parameters.AddWithValue("@ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
        uCmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    private void TryBackupAndDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var backup = path + ".corrupt." + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                File.Move(path, backup, overwrite: true);
                _logger?.Warning("Persistence", $"Corrupted database renamed to '{backup}' — fresh database will be created");

                // Also remove the WAL/SHM shim files
                if (File.Exists(path + "-wal"))  File.Delete(path + "-wal");
                if (File.Exists(path + "-shm"))  File.Delete(path + "-shm");
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("Persistence", $"Failed to rename corrupted database '{path}'", ex);
        }
    }
}
