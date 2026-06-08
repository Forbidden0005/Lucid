using FluentAssertions;
using Lucid.Services.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lucid.Tests.Persistence;

/// <summary>
/// Exercises the real SQLite persistence queue against an isolated temp
/// database. These tests cover schema initialization, batch draining, and the
/// final flush path that protects queued writes during shutdown.
/// </summary>
public sealed class SQLitePersistenceServiceTests
{
    [Fact]
    public async Task InitializeAsync_CreatesSchemaMetadata_AndMarksServiceHealthy()
    {
        using var dir = new TestDir();
        using var service = CreateService(dir.DatabasePath);

        await service.InitializeAsync();

        service.IsHealthy.Should().BeTrue();
        service.DatabasePath.Should().Be(dir.DatabasePath);

        var schemaVersion = await service.QueryAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM db_metadata WHERE key = 'schema_version';";
            return cmd.ExecuteScalar() as string;
        });

        schemaVersion.Should().Be("1");
    }

    [Fact]
    public async Task FlushQueueAsync_PersistsQueuedWrites_AndRecordsFlush()
    {
        using var dir = new TestDir();
        using var service = CreateService(dir.DatabasePath);
        await service.InitializeAsync();

        service.EnqueueWrite(conn => InsertMetadata(conn, "queue-test-a", "1"));
        service.EnqueueWrite(conn => InsertMetadata(conn, "queue-test-b", "2"));

        await service.FlushQueueAsync();

        service.PendingWriteCount.Should().Be(0);
        service.QueueMetrics.TotalWriteCount.Should().Be(2);
        service.QueueMetrics.TotalFlushCount.Should().Be(1);

        var count = await CountMetadataRowsAsync(service, "queue-test-%");
        count.Should().Be(2);
    }

    [Fact]
    public async Task FlushQueueAsync_HonorsBatchLimit_AndLeavesRemainderQueued()
    {
        using var dir = new TestDir();
        using var service = CreateService(dir.DatabasePath);
        await service.InitializeAsync();

        for (int i = 0; i < 501; i++)
        {
            var key = $"batch-key-{i:D3}";
            service.EnqueueWrite(conn => InsertMetadata(conn, key, i.ToString()));
        }

        await service.FlushQueueAsync();

        service.PendingWriteCount.Should().Be(1);
        service.QueueMetrics.TotalFlushCount.Should().Be(1);
        (await CountMetadataRowsAsync(service, "batch-key-%")).Should().Be(500);

        await service.FlushQueueAsync();

        service.PendingWriteCount.Should().Be(0);
        service.QueueMetrics.TotalFlushCount.Should().Be(2);
        (await CountMetadataRowsAsync(service, "batch-key-%")).Should().Be(501);
    }

    [Fact]
    public async Task Dispose_PerformsFinalFlush_ForPendingWrites()
    {
        using var dir = new TestDir();
        var service = CreateService(dir.DatabasePath);
        await service.InitializeAsync();
        service.EnqueueWrite(conn => InsertMetadata(conn, "dispose-test", "ok"));

        service.Dispose();

        await using var conn = OpenConnection(dir.DatabasePath);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM db_metadata WHERE key = 'dispose-test';";

        (cmd.ExecuteScalar() as string).Should().Be("ok");
    }

    [Fact]
    public async Task InitializeAsync_MigratesLegacySchemaVersionZeroDatabase()
    {
        using var dir = new TestDir();
        SeedLegacyVersionZeroDatabase(dir.DatabasePath);
        using var service = CreateService(dir.DatabasePath);

        await service.InitializeAsync();

        service.IsHealthy.Should().BeTrue();
        (await ReadSchemaVersionAsync(service)).Should().Be("1");

        var telemetryTableExists = await service.QueryAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type = 'table' AND name = 'telemetry_samples';
                """;
            return Convert.ToInt32(cmd.ExecuteScalar());
        });

        telemetryTableExists.Should().Be(1);
    }

    [Fact]
    public async Task InitializeAsync_DoesNotRewriteExistingVersionOneMetadata()
    {
        using var dir = new TestDir();
        const string expectedCreatedAt = "1234567890";
        SeedVersionOneDatabase(dir.DatabasePath, expectedCreatedAt);
        using var service = CreateService(dir.DatabasePath);

        await service.InitializeAsync();

        service.IsHealthy.Should().BeTrue();
        (await ReadSchemaVersionAsync(service)).Should().Be("1");

        var createdAt = await service.QueryAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM db_metadata WHERE key = 'created_at';";
            return cmd.ExecuteScalar() as string;
        });

        createdAt.Should().Be(expectedCreatedAt,
            "re-initializing a current-schema database should not rewrite metadata");
    }

    private static SQLitePersistenceService CreateService(string dbPath) =>
        new(dbPath, logger: null, startFlushTimer: false);

    private static async Task<string?> ReadSchemaVersionAsync(SQLitePersistenceService service) =>
        await service.QueryAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM db_metadata WHERE key = 'schema_version';";
            return cmd.ExecuteScalar() as string;
        });

    private static async Task<int> CountMetadataRowsAsync(SQLitePersistenceService service, string keyLike)
    {
        var result = await service.QueryAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM db_metadata WHERE key LIKE @keyLike;";
            cmd.Parameters.AddWithValue("@keyLike", keyLike);
            return Convert.ToInt32(cmd.ExecuteScalar());
        });

        return result;
    }

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

    private static SqliteConnection OpenConnection(string dbPath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        };

        return new SqliteConnection(builder.ToString());
    }

    private static void SeedLegacyVersionZeroDatabase(string dbPath)
    {
        using var conn = OpenConnection(dbPath);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS db_metadata (
                key   TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL
            );
            INSERT INTO db_metadata (key, value) VALUES ('schema_version', '0')
            ON CONFLICT(key) DO UPDATE SET value = '0';
            """;
        cmd.ExecuteNonQuery();
    }

    private static void SeedVersionOneDatabase(string dbPath, string createdAt)
    {
        using var conn = OpenConnection(dbPath);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS db_metadata (
                key   TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL
            );
            INSERT INTO db_metadata (key, value) VALUES ('schema_version', '1')
            ON CONFLICT(key) DO UPDATE SET value = '1';
            INSERT INTO db_metadata (key, value) VALUES ('created_at', @createdAt)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        cmd.Parameters.AddWithValue("@createdAt", createdAt);
        cmd.ExecuteNonQuery();
    }

    private sealed class TestDir : IDisposable
    {
        public TestDir()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "lucid-sqlite-persistence-tests",
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
            }
        }
    }
}
