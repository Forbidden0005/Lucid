using FluentAssertions;
using Lucid.Services.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lucid.Tests.Persistence;

/// <summary>
/// Durability and fault-tolerance coverage for <see cref="SQLitePersistenceService"/>.
///
/// Complements <see cref="SQLitePersistenceServiceTests"/> (schema, batching,
/// shutdown flush) with the failure-path behavior the platform's explainability
/// doctrine depends on:
///   • Queue overflow must apply visible back-pressure — drop, count, notify —
///     rather than grow unbounded.
///   • A corrupted database file must be backed up and recreated, never crash
///     the application or silently lose the recovery evidence.
///   • A single failing queued write must not poison the rest of its batch.
///   • Writes attempted before initialization or after disposal must be
///     ignored safely instead of throwing.
/// </summary>
public sealed class SQLitePersistenceDurabilityTests
{
    /// <summary>
    /// Mirrors SQLitePersistenceService.MaxQueueDepth (private const).
    /// If the production constant changes, the overflow test below fails with
    /// an explicit count mismatch, prompting a deliberate update here.
    /// </summary>
    private const int MaxQueueDepth = 2000;

    // ── Queue overflow back-pressure ──────────────────────────────────────────

    [Fact]
    public async Task EnqueueWrite_AtMaxQueueDepth_DropsWrite_AndRecordsVisibleBackPressure()
    {
        using var dir = new TestDir();
        using var service = CreateService(dir.DatabasePath);
        await service.InitializeAsync();

        (int Depth, int Max)? droppedCallbackArgs = null;
        service.OnWriteDropped = (depth, max) => droppedCallbackArgs = (depth, max);

        // Fill the queue to capacity, then attempt one write past the limit.
        for (int i = 0; i < MaxQueueDepth; i++)
            service.EnqueueWrite(_ => { });

        service.EnqueueWrite(_ => { });

        service.PendingWriteCount.Should().Be(MaxQueueDepth,
            "the queue must be bounded at its configured capacity");
        service.QueueMetrics.TotalWriteCount.Should().Be(MaxQueueDepth,
            "only accepted writes count as enqueued");
        service.QueueMetrics.DroppedWriteCount.Should().Be(1,
            "the over-capacity write must be recorded as dropped, not lost invisibly");
        service.QueueMetrics.LastDroppedAt.Should().NotBeNull();
        droppedCallbackArgs.Should().Be((MaxQueueDepth, MaxQueueDepth),
            "the drop callback must report current depth and configured maximum");
    }

    [Fact]
    public async Task EnqueueWrite_AfterOverflow_AcceptsNewWritesOnceQueueDrains()
    {
        using var dir = new TestDir();
        using var service = CreateService(dir.DatabasePath);
        await service.InitializeAsync();

        for (int i = 0; i < MaxQueueDepth + 5; i++)
            service.EnqueueWrite(_ => { });

        service.QueueMetrics.DroppedWriteCount.Should().Be(5);

        // Drain the queue (MaxFlushBatchSize is 500, so 4 flushes clear 2000).
        for (int i = 0; i < 4; i++)
            await service.FlushQueueAsync();

        service.PendingWriteCount.Should().Be(0);

        // Back-pressure must be transient: a drained queue accepts writes again.
        service.EnqueueWrite(conn => InsertMetadata(conn, "post-overflow", "ok"));
        await service.FlushQueueAsync();

        service.QueueMetrics.DroppedWriteCount.Should().Be(5,
            "no further drops should occur after the queue drains");
        (await ReadMetadataAsync(service, "post-overflow")).Should().Be("ok");
    }

    // ── Corrupt database recovery ─────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_WithCorruptDatabaseFile_BacksUpAndRecreates()
    {
        using var dir = new TestDir();

        // A file that is not a SQLite database: pragmas fail on first touch,
        // which must route initialization through the corruption-recovery path.
        File.WriteAllText(dir.DatabasePath, "this is not a sqlite database");

        // Stale WAL/SHM shims from the corrupt incarnation must also be removed,
        // otherwise the fresh database can inherit poisoned journal state.
        File.WriteAllText(dir.DatabasePath + "-wal", "stale wal");
        File.WriteAllText(dir.DatabasePath + "-shm", "stale shm");

        using var service = CreateService(dir.DatabasePath);
        await service.InitializeAsync();

        service.IsHealthy.Should().BeTrue(
            "recovery must yield a working database, not a disabled service");

        var backups = Directory.GetFiles(dir.RootPath, "explainmypc.db.corrupt.*");
        backups.Should().HaveCount(1,
            "the corrupted file must be preserved as evidence, not deleted");
        File.ReadAllText(backups[0]).Should().Be("this is not a sqlite database");

        // The recreated database must be fully migrated and writable.
        (await ReadSchemaVersionAsync(service)).Should().Be("1");
        service.EnqueueWrite(conn => InsertMetadata(conn, "post-recovery", "ok"));
        await service.FlushQueueAsync();
        (await ReadMetadataAsync(service, "post-recovery")).Should().Be("ok");

        // While the recovered connection is open, WAL mode legitimately maintains
        // live -wal/-shm files, so existence alone proves nothing about staleness.
        // After a clean close SQLite checkpoints and unlinks shims it owns — the
        // poisoned pre-recovery files must not survive that. (Dispose is
        // idempotent, so the later using-dispose is a safe no-op.)
        service.Dispose();
        File.Exists(dir.DatabasePath + "-wal").Should().BeFalse(
            "stale WAL shim must not survive recovery and a clean close");
        File.Exists(dir.DatabasePath + "-shm").Should().BeFalse(
            "stale SHM shim must not survive recovery and a clean close");
    }

    // ── Poison-write isolation in batch flush ─────────────────────────────────

    [Fact]
    public async Task FlushQueueAsync_WithThrowingWriteInBatch_CommitsRemainingWrites()
    {
        using var dir = new TestDir();
        using var service = CreateService(dir.DatabasePath);
        await service.InitializeAsync();

        service.EnqueueWrite(conn => InsertMetadata(conn, "before-poison", "1"));
        service.EnqueueWrite(_ => throw new InvalidOperationException("poison write"));
        service.EnqueueWrite(conn => InsertMetadata(conn, "after-poison", "2"));

        await service.FlushQueueAsync();

        service.PendingWriteCount.Should().Be(0,
            "a throwing write must be consumed, not retried forever");
        service.QueueMetrics.TotalFlushCount.Should().Be(1);
        (await ReadMetadataAsync(service, "before-poison")).Should().Be("1");
        (await ReadMetadataAsync(service, "after-poison")).Should().Be("2",
            "writes after a poison write in the same batch must still commit");
    }

    // ── Lifecycle write gating ────────────────────────────────────────────────

    [Fact]
    public void EnqueueWrite_BeforeInitialize_IsIgnoredWithoutThrowing()
    {
        using var dir = new TestDir();
        using var service = CreateService(dir.DatabasePath);

        // _healthy is false until InitializeAsync succeeds — writes must no-op.
        var act = () => service.EnqueueWrite(conn => InsertMetadata(conn, "too-early", "x"));

        act.Should().NotThrow();
        service.PendingWriteCount.Should().Be(0);
        service.QueueMetrics.TotalWriteCount.Should().Be(0,
            "writes rejected by the health gate must not count as enqueued");
    }

    [Fact]
    public async Task Dispose_WithDanglingRolledBackTransaction_DoesNotThrow()
    {
        // Field crash 2026-07-03 (via crash.log): closing the app disposed the
        // connection while a SqliteTransaction object was still attached whose
        // underlying transaction SQLite had already rolled back. Close() then
        // disposed the dangling transaction and RollbackInternal threw
        // "cannot rollback - no transaction is active" — crashing the exit.
        using var dir = new TestDir();
        var service = CreateService(dir.DatabasePath);
        await service.InitializeAsync();

        // Reconstruct that exact state: open an ADO transaction, roll back the
        // UNDERLYING transaction behind its back, and abandon the object.
        await service.QueryAsync<object?>(conn =>
        {
            var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "ROLLBACK;";
            cmd.ExecuteNonQuery();
            return null;   // tx deliberately not disposed — it dangles on the connection
        });

        var act = () => service.Dispose();

        act.Should().NotThrow(
            "shutdown must never crash the app — WAL recovery reconciles on next open");
    }

    [Fact]
    public async Task EnqueueWrite_AfterDispose_IsIgnoredWithoutThrowing()
    {
        using var dir = new TestDir();
        var service = CreateService(dir.DatabasePath);
        await service.InitializeAsync();
        service.Dispose();

        var act = () => service.EnqueueWrite(conn => InsertMetadata(conn, "too-late", "x"));

        act.Should().NotThrow();
        service.PendingWriteCount.Should().Be(0);
    }

    [Fact]
    public async Task QueryAsync_WhenQueryThrows_ReturnsDefaultInsteadOfPropagating()
    {
        using var dir = new TestDir();
        using var service = CreateService(dir.DatabasePath);
        await service.InitializeAsync();

        var result = await service.QueryAsync<string?>(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM table_that_does_not_exist;";
            return cmd.ExecuteScalar() as string;
        });

        result.Should().BeNull("read failures must degrade to default, not crash callers");
        service.IsHealthy.Should().BeTrue("a failed query must not poison service health");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SQLitePersistenceService CreateService(string dbPath) =>
        new(dbPath, logger: null, startFlushTimer: false);

    private static async Task<string?> ReadSchemaVersionAsync(SQLitePersistenceService service) =>
        await ReadMetadataAsync(service, "schema_version");

    private static async Task<string?> ReadMetadataAsync(SQLitePersistenceService service, string key) =>
        await service.QueryAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM db_metadata WHERE key = @key;";
            cmd.Parameters.AddWithValue("@key", key);
            return cmd.ExecuteScalar() as string;
        });

    private static void InsertMetadata(SqliteConnection conn, string key, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO db_metadata (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.ExecuteNonQuery();
    }

    private sealed class TestDir : IDisposable
    {
        public TestDir()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "lucid-sqlite-durability-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
            DatabasePath = Path.Combine(RootPath, "explainmypc.db");
        }

        public string RootPath { get; }
        public string DatabasePath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                    Directory.Delete(RootPath, recursive: true);
            }
            catch
            {
                // best-effort: temp cleanup failure must not fail the test run
            }
        }
    }
}
