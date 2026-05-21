using Microsoft.Data.Sqlite;

namespace ExplainMyPC.Services.Persistence;

/// <summary>
/// Persists and retrieves hardware telemetry samples from the SQLite database.
///
/// Write strategy:
///   • Callers call <see cref="EnqueueSample"/> every ~30 seconds.
///   • Enqueued writes are batched by <see cref="SQLitePersistenceService"/> and
///     committed in a single transaction every 30 seconds.
///
/// Downsampling (retention):
///   <see cref="DownsampleAndPurgeAsync"/> aggregates and evicts old rows.
///   Should be called once per hour (from AppServices, low-priority).
///
///   Resolution  → Retained for
///   Raw30s        48 hours
///   Min1          14 days
///   Min15         90 days
///   Hour1         2 years
///
/// Read strategy:
///   <see cref="GetSamplesAsync"/> returns the best-resolution rows for a given
///   time range.  For short ranges (≤ 24h) it prefers raw samples; for longer
///   ranges it uses coarser aggregates.
/// </summary>
public sealed class HistoricalTelemetryRepository
{
    // ── Retention limits ──────────────────────────────────────────────────────

    private static readonly TimeSpan RetainRaw30s = TimeSpan.FromHours(48);
    private static readonly TimeSpan RetainMin1   = TimeSpan.FromDays(14);
    private static readonly TimeSpan RetainMin15  = TimeSpan.FromDays(90);
    private static readonly TimeSpan RetainHour1  = TimeSpan.FromDays(730);  // 2 years

    // ── Throttle — write at most one sample per window ────────────────────────

    private const int SampleWindowSeconds = 30;
    private DateTimeOffset _lastSampleWritten = DateTimeOffset.MinValue;

    private readonly SQLitePersistenceService _db;

    public HistoricalTelemetryRepository(SQLitePersistenceService db) => _db = db;

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Enqueues a telemetry sample for batch-write to SQLite.
    /// Throttled to one row per <see cref="SampleWindowSeconds"/> to avoid write amplification.
    /// </summary>
    public void EnqueueSample(
        double  cpuPct,
        double  ramPct,
        double? gpuPct,
        double  diskPct,
        double? tempC)
    {
        var now = DateTimeOffset.UtcNow;
        if ((now - _lastSampleWritten).TotalSeconds < SampleWindowSeconds) return;
        _lastSampleWritten = now;

        long ts  = now.ToUnixTimeMilliseconds();
        int  res = (int)TelemetrySampleResolution.Raw30s;

        _db.EnqueueWrite(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO telemetry_samples
                    (sampled_at, resolution, cpu_pct, ram_pct, gpu_pct, disk_pct, temp_c, sample_count)
                VALUES (@ts, @res, @cpu, @ram, @gpu, @disk, @temp, 1);
                """;
            cmd.Parameters.AddWithValue("@ts",   ts);
            cmd.Parameters.AddWithValue("@res",  res);
            cmd.Parameters.AddWithValue("@cpu",  cpuPct);
            cmd.Parameters.AddWithValue("@ram",  ramPct);
            cmd.Parameters.AddWithValue("@gpu",  gpuPct.HasValue ? (object)gpuPct.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@disk", diskPct);
            cmd.Parameters.AddWithValue("@temp", tempC.HasValue ? (object)tempC.Value   : DBNull.Value);
            cmd.ExecuteNonQuery();
        });
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns samples covering the given time range, automatically selecting
    /// the most appropriate resolution tier.
    ///
    /// Range → resolution selected:
    ///   ≤ 48h    → Raw30s
    ///   ≤ 14d    → Min1
    ///   ≤ 90d    → Min15
    ///   > 90d    → Hour1
    /// </summary>
    public async Task<IReadOnlyList<PersistedTelemetrySample>> GetSamplesAsync(
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var span = to - from;
        var res  = span switch
        {
            _ when span.TotalHours  <= 48  => TelemetrySampleResolution.Raw30s,
            _ when span.TotalDays   <= 14  => TelemetrySampleResolution.Min1,
            _ when span.TotalDays   <= 90  => TelemetrySampleResolution.Min15,
            _                              => TelemetrySampleResolution.Hour1,
        };

        return await _db.QueryAsync<IReadOnlyList<PersistedTelemetrySample>>(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, sampled_at, resolution, cpu_pct, ram_pct, gpu_pct, disk_pct, temp_c, sample_count
                FROM   telemetry_samples
                WHERE  resolution = @res
                  AND  sampled_at BETWEEN @from AND @to
                ORDER  BY sampled_at ASC;
                """;
            cmd.Parameters.AddWithValue("@res",  (int)res);
            cmd.Parameters.AddWithValue("@from", from.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("@to",   to.ToUnixTimeMilliseconds());

            return ReadSamples(cmd);
        }) ?? [];
    }

    /// <summary>
    /// Returns the earliest telemetry timestamp in the database, or null when
    /// no samples have been written yet.
    /// </summary>
    public async Task<DateTimeOffset?> GetOldestSampleTimeAsync()
    {
        var ms = await _db.QueryAsync<long?>(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT MIN(sampled_at) FROM telemetry_samples;";
            var raw = cmd.ExecuteScalar();
            return raw is long l ? l : (long?)null;
        });

        return ms.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(ms.Value) : null;
    }

    /// <summary>
    /// Returns total number of telemetry rows stored (all resolutions).
    /// </summary>
    public async Task<long> GetTotalRowCountAsync()
    {
        return await _db.QueryAsync<long>(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM telemetry_samples;";
            return (long)(cmd.ExecuteScalar() ?? 0L);
        });
    }

    // ── Downsampling & purge ──────────────────────────────────────────────────

    /// <summary>
    /// Aggregates raw samples older than <see cref="RetainRaw30s"/> into 1-minute
    /// aggregates, 1-minute rows older than <see cref="RetainMin1"/> into 15-minute
    /// rows, etc., then purges rows that exceed their retention window.
    ///
    /// Designed to run as a low-priority background task once per hour.
    /// All operations are idempotent and safe to interrupt.
    /// </summary>
    public async Task DownsampleAndPurgeAsync()
    {
        if (!_db.IsHealthy) return;

        // Run all downsampling steps sequentially inside a single direct write.
        await _db.ExecuteDirectAsync(conn =>
        {
            var now = DateTimeOffset.UtcNow;

            // 1. Aggregate Raw30s → Min1 (rows older than RetainRaw30s)
            AggregateInto(conn,
                fromRes:    (int)TelemetrySampleResolution.Raw30s,
                toRes:      (int)TelemetrySampleResolution.Min1,
                olderThanMs: (now - RetainRaw30s).ToUnixTimeMilliseconds(),
                bucketMs:   60_000L);

            // 2. Aggregate Min1 → Min15
            AggregateInto(conn,
                fromRes:    (int)TelemetrySampleResolution.Min1,
                toRes:      (int)TelemetrySampleResolution.Min15,
                olderThanMs: (now - RetainMin1).ToUnixTimeMilliseconds(),
                bucketMs:   900_000L);

            // 3. Aggregate Min15 → Hour1
            AggregateInto(conn,
                fromRes:    (int)TelemetrySampleResolution.Min15,
                toRes:      (int)TelemetrySampleResolution.Hour1,
                olderThanMs: (now - RetainMin15).ToUnixTimeMilliseconds(),
                bucketMs:   3_600_000L);

            // 4. Purge Hour1 rows older than 2 years
            using var purge = conn.CreateCommand();
            purge.CommandText = """
                DELETE FROM telemetry_samples
                WHERE resolution = @res AND sampled_at < @cutoff;
                """;
            purge.Parameters.AddWithValue("@res",    (int)TelemetrySampleResolution.Hour1);
            purge.Parameters.AddWithValue("@cutoff", (now - RetainHour1).ToUnixTimeMilliseconds());
            purge.ExecuteNonQuery();
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static void AggregateInto(
        SqliteConnection conn,
        int  fromRes,
        int  toRes,
        long olderThanMs,
        long bucketMs)
    {
        // Find oldest un-aggregated bucket start
        using var insCmd = conn.CreateCommand();
        insCmd.CommandText = """
            INSERT OR IGNORE INTO telemetry_samples
                (sampled_at, resolution, cpu_pct, ram_pct, gpu_pct, disk_pct, temp_c, sample_count)
            SELECT
                (sampled_at / @bucket) * @bucket AS bucket_start,
                @toRes,
                AVG(cpu_pct),
                AVG(ram_pct),
                AVG(gpu_pct),
                AVG(disk_pct),
                AVG(temp_c),
                COUNT(*)
            FROM   telemetry_samples
            WHERE  resolution = @fromRes
              AND  sampled_at < @cutoff
            GROUP  BY (sampled_at / @bucket);
            """;
        insCmd.Parameters.AddWithValue("@bucket",  bucketMs);
        insCmd.Parameters.AddWithValue("@toRes",   toRes);
        insCmd.Parameters.AddWithValue("@fromRes", fromRes);
        insCmd.Parameters.AddWithValue("@cutoff",  olderThanMs);
        insCmd.ExecuteNonQuery();

        // Delete source rows that have been aggregated
        using var delCmd = conn.CreateCommand();
        delCmd.CommandText = """
            DELETE FROM telemetry_samples
            WHERE resolution = @fromRes AND sampled_at < @cutoff;
            """;
        delCmd.Parameters.AddWithValue("@fromRes", fromRes);
        delCmd.Parameters.AddWithValue("@cutoff",  olderThanMs);
        delCmd.ExecuteNonQuery();
    }

    private static List<PersistedTelemetrySample> ReadSamples(SqliteCommand cmd)
    {
        var results = new List<PersistedTelemetrySample>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new PersistedTelemetrySample
            {
                Id          = reader.GetInt64(0),
                SampledAt   = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
                Resolution  = (TelemetrySampleResolution)reader.GetInt32(2),
                CpuPercent  = reader.GetDouble(3),
                RamPercent  = reader.GetDouble(4),
                GpuPercent  = reader.IsDBNull(5) ? null : reader.GetDouble(5),
                DiskPercent = reader.GetDouble(6),
                TempCelsius = reader.IsDBNull(7) ? null : reader.GetDouble(7),
                SampleCount = reader.GetInt32(8),
            });
        }
        return results;
    }
}
