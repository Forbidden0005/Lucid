using ExplainMyPC.Services.Intelligence;
using Microsoft.Data.Sqlite;

namespace ExplainMyPC.Services.Persistence;

/// <summary>
/// Persists the onset and resolution history for each intelligence finding.
///
/// Each time an insight becomes active a new row is inserted (onset_at set,
/// resolved_at = NULL). When the insight clears, the open row is updated.
/// Because the same insight_id can appear multiple times across separate activations,
/// the table may have many rows per insight_id.
///
/// Writes are DIRECT (not batched) because insight history integrity is important —
/// a batched write that is dropped before flush would lose the record permanently.
///
/// Retention: rows older than <see cref="RetentionDays"/> days are purged periodically.
/// </summary>
public sealed class InsightHistoryRepository
{
    private const int RetentionDays = 365;

    private readonly SQLitePersistenceService _db;

    /// <summary>
    /// Maps insight_id → open row id for insights that are currently active.
    /// Used to resolve the correct row when an insight clears.
    /// </summary>
    private readonly Dictionary<string, long> _openRows = new(StringComparer.Ordinal);

    public InsightHistoryRepository(SQLitePersistenceService db) => _db = db;

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Records the onset of an insight.  If this insight_id already has an open
    /// (unresolved) row in the database, the call is a no-op (prevents duplicates
    /// from rapid re-firing of the same rule).
    /// </summary>
    public async Task RecordOnsetAsync(SystemInsight insight)
    {
        // Don't track the "everything is fine" insight
        if (insight.Id == "system.healthy") return;

        // Don't duplicate if already tracked in-memory
        if (_openRows.ContainsKey(insight.Id)) return;

        long ts = insight.DetectedAt.ToUnixTimeMilliseconds();

        // Insert and capture the new rowid
        long? rowId = null;
        await _db.ExecuteDirectAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO insight_history (insight_id, title, severity, onset_at)
                VALUES (@id, @title, @sev, @ts);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("@id",    insight.Id);
            cmd.Parameters.AddWithValue("@title", insight.Title);
            cmd.Parameters.AddWithValue("@sev",   (int)insight.Severity);
            cmd.Parameters.AddWithValue("@ts",    ts);
            rowId = (long)(cmd.ExecuteScalar() ?? 0L);
        }).ConfigureAwait(false);

        if (rowId.HasValue && rowId.Value > 0)
            _openRows[insight.Id] = rowId.Value;
    }

    /// <summary>
    /// Records the resolution of an insight, closing the open row.
    /// No-op if no open row was tracked for this insight_id.
    /// </summary>
    public async Task RecordResolutionAsync(string insightId)
    {
        if (!_openRows.TryGetValue(insightId, out long rowId))
            return;

        _openRows.Remove(insightId);

        long resolvedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.ExecuteDirectAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE insight_history
                SET    resolved_at      = @resolved,
                       duration_seconds = CAST((@resolved - onset_at) / 1000 AS INTEGER)
                WHERE  id = @rowId;
                """;
            cmd.Parameters.AddWithValue("@resolved", resolvedMs);
            cmd.Parameters.AddWithValue("@rowId",    rowId);
            cmd.ExecuteNonQuery();
        }).ConfigureAwait(false);
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all insight history rows within the given time range (onset_at based),
    /// newest-first.
    /// </summary>
    public async Task<IReadOnlyList<PersistedInsightRecord>> GetInsightHistoryAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        int            maxCount = 5000)
    {
        return await _db.QueryAsync<IReadOnlyList<PersistedInsightRecord>>(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, insight_id, title, severity, onset_at, resolved_at, duration_seconds
                FROM   insight_history
                WHERE  onset_at BETWEEN @from AND @to
                ORDER  BY onset_at DESC
                LIMIT  @limit;
                """;
            cmd.Parameters.AddWithValue("@from",  from.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("@to",    to.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("@limit", maxCount);

            return ReadRecords(cmd);
        }) ?? [];
    }

    /// <summary>
    /// Returns all distinct insight_ids, together with their occurrence count,
    /// last onset time, and average duration, over the past <paramref name="days"/> days.
    /// Ordered by occurrence count descending.
    /// </summary>
    public async Task<IReadOnlyList<(string InsightId, string Title, int Occurrences, DateTimeOffset LastSeen, int AvgDurationSecs)>>
        GetRecurringInsightsAsync(int days = 30, int maxCount = 20)
    {
        long from = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromDays(days)).ToUnixTimeMilliseconds();

        return await _db.QueryAsync<IReadOnlyList<(string, string, int, DateTimeOffset, int)>>(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT   insight_id,
                         MAX(title)            AS title,
                         COUNT(*)              AS occurrences,
                         MAX(onset_at)         AS last_seen,
                         COALESCE(AVG(duration_seconds), 0) AS avg_dur
                FROM     insight_history
                WHERE    onset_at >= @from
                  AND    insight_id != 'system.healthy'
                GROUP BY insight_id
                ORDER BY occurrences DESC
                LIMIT    @limit;
                """;
            cmd.Parameters.AddWithValue("@from",  from);
            cmd.Parameters.AddWithValue("@limit", maxCount);

            var results = new List<(string, string, int, DateTimeOffset, int)>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                results.Add((
                    r.GetString(0),
                    r.GetString(1),
                    r.GetInt32(2),
                    DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(3)),
                    (int)r.GetDouble(4)));
            }
            return results;
        }) ?? [];
    }

    /// <summary>Returns total number of insight history rows.</summary>
    public async Task<long> GetTotalRowCountAsync()
    {
        return await _db.QueryAsync<long>(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM insight_history;";
            return (long)(cmd.ExecuteScalar() ?? 0L);
        });
    }

    // ── Maintenance ───────────────────────────────────────────────────────────

    /// <summary>Purges insight history rows older than <see cref="RetentionDays"/> days.</summary>
    public void PurgeOldRecords()
    {
        long cutoff = DateTimeOffset.UtcNow
            .Subtract(TimeSpan.FromDays(RetentionDays))
            .ToUnixTimeMilliseconds();

        _db.EnqueueWrite(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM insight_history WHERE onset_at < @cutoff;";
            cmd.Parameters.AddWithValue("@cutoff", cutoff);
            cmd.ExecuteNonQuery();
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static List<PersistedInsightRecord> ReadRecords(SqliteCommand cmd)
    {
        var results = new List<PersistedInsightRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            long?   resolvedMs = reader.IsDBNull(5) ? null : reader.GetInt64(5);
            int?    dur        = reader.IsDBNull(6) ? null : reader.GetInt32(6);

            results.Add(new PersistedInsightRecord
            {
                Id              = reader.GetInt64(0),
                InsightId       = reader.GetString(1),
                Title           = reader.GetString(2),
                SeverityOrdinal = reader.GetInt32(3),
                OnsetAt         = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)),
                ResolvedAt      = resolvedMs.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(resolvedMs.Value) : null,
                DurationSeconds = dur,
            });
        }
        return results;
    }
}
