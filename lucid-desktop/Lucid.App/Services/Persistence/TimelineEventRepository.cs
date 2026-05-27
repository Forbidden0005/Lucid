using Lucid.Services.Timeline;
using Microsoft.Data.Sqlite;

namespace Lucid.Services.Persistence;

/// <summary>
/// Persists and retrieves <see cref="TimelineEvent"/> records from the SQLite database.
///
/// Write strategy:
///   Events are enqueued via <see cref="EnqueueEvent"/> and flushed in batches
///   by <see cref="SQLitePersistenceService"/> every 30 seconds.
///   INSERT OR IGNORE prevents duplicate persistence (events with the same Id are skipped).
///
/// Read strategy:
///   <see cref="GetEventsAsync"/> returns events in a time range, newest-first,
///   capped to a caller-specified limit.
///
/// Retention:
///   Events older than <see cref="RetentionDays"/> are purged by
///   <see cref="PurgeOldEventsAsync"/>, called periodically by AppServices.
/// </summary>
public sealed class TimelineEventRepository
{
    private const int RetentionDays = 90;

    private readonly SQLitePersistenceService _db;

    public TimelineEventRepository(SQLitePersistenceService db) => _db = db;

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Enqueues the event for batch persistence.
    /// Duplicate event IDs are silently ignored (INSERT OR IGNORE).
    /// </summary>
    public void EnqueueEvent(TimelineEvent ev)
    {
        long ts = ev.OccurredAt.ToUnixTimeMilliseconds();
        string? procs = ev.AttributedProcesses is { Count: > 0 }
            ? string.Join(",", ev.AttributedProcesses)
            : null;

        _db.EnqueueWrite(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO timeline_events
                    (event_id, event_type, severity, title, detail,
                     occurred_at, related_insight_id, attributed_processes, action_id)
                VALUES
                    (@id, @type, @sev, @title, @detail,
                     @ts, @insight, @procs, @action);
                """;
            cmd.Parameters.AddWithValue("@id",      ev.Id);
            cmd.Parameters.AddWithValue("@type",    (int)ev.Type);
            cmd.Parameters.AddWithValue("@sev",     (int)ev.Severity);
            cmd.Parameters.AddWithValue("@title",   ev.Title);
            cmd.Parameters.AddWithValue("@detail",  ev.Detail    is null ? DBNull.Value : ev.Detail);
            cmd.Parameters.AddWithValue("@ts",      ts);
            cmd.Parameters.AddWithValue("@insight", ev.RelatedInsightId is null ? DBNull.Value : ev.RelatedInsightId);
            cmd.Parameters.AddWithValue("@procs",   procs is null ? DBNull.Value : procs);
            cmd.Parameters.AddWithValue("@action",  ev.ActionId  is null ? DBNull.Value : ev.ActionId);
            cmd.ExecuteNonQuery();
        });
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns events within the given time range, newest-first, limited to
    /// <paramref name="maxCount"/> rows.
    /// </summary>
    public async Task<IReadOnlyList<PersistedTimelineEvent>> GetEventsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        int            maxCount = 1000)
    {
        return await _db.QueryAsync<IReadOnlyList<PersistedTimelineEvent>>(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT event_id, event_type, severity, title, detail,
                       occurred_at, related_insight_id, attributed_processes, action_id
                FROM   timeline_events
                WHERE  occurred_at BETWEEN @from AND @to
                ORDER  BY occurred_at DESC
                LIMIT  @limit;
                """;
            cmd.Parameters.AddWithValue("@from",  from.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("@to",    to.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("@limit", maxCount);

            return ReadEvents(cmd);
        }) ?? [];
    }

    /// <summary>Returns total number of timeline events stored.</summary>
    public async Task<long> GetTotalEventCountAsync()
    {
        return await _db.QueryAsync<long>(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM timeline_events;";
            return (long)(cmd.ExecuteScalar() ?? 0L);
        });
    }

    // ── Maintenance ───────────────────────────────────────────────────────────

    /// <summary>
    /// Deletes events older than <see cref="RetentionDays"/> days.
    /// Safe to call periodically; runs in the background write queue.
    /// </summary>
    public void PurgeOldEvents()
    {
        long cutoff = DateTimeOffset.UtcNow
            .Subtract(TimeSpan.FromDays(RetentionDays))
            .ToUnixTimeMilliseconds();

        _db.EnqueueWrite(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM timeline_events WHERE occurred_at < @cutoff;";
            cmd.Parameters.AddWithValue("@cutoff", cutoff);
            cmd.ExecuteNonQuery();
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static List<PersistedTimelineEvent> ReadEvents(SqliteCommand cmd)
    {
        var results = new List<PersistedTimelineEvent>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new PersistedTimelineEvent
            {
                EventId              = reader.GetString(0),
                EventTypeOrdinal     = reader.GetInt32(1),
                SeverityOrdinal      = reader.GetInt32(2),
                Title                = reader.GetString(3),
                Detail               = reader.IsDBNull(4)  ? null : reader.GetString(4),
                OccurredAt           = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5)),
                RelatedInsightId     = reader.IsDBNull(6)  ? null : reader.GetString(6),
                AttributedProcesses  = reader.IsDBNull(7)  ? null : reader.GetString(7),
                ActionId             = reader.IsDBNull(8)  ? null : reader.GetString(8),
            });
        }
        return results;
    }
}
