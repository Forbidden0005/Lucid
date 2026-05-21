using ExplainMyPC.Services.Learning;
using Microsoft.Data.Sqlite;

namespace ExplainMyPC.Services.Persistence;

/// <summary>
/// SQLite-backed storage for <see cref="ActionOutcomeRecord"/> instances.
/// Replaces the JSON file used by <see cref="Learning.RemediationLearningService"/>
/// with a durable, queryable database table.
///
/// Writes are DIRECT (not batched) because outcome records are important:
/// losing one means a permanently unanalyzed action record.
///
/// The table is keyed by <c>operation_id</c> (unique per OperationRecord.Id),
/// so writing the same outcome twice is idempotent (INSERT OR REPLACE).
/// </summary>
public sealed class RecommendationOutcomeRepository
{
    private readonly SQLitePersistenceService _db;

    public RecommendationOutcomeRepository(SQLitePersistenceService db) => _db = db;

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts or replaces the outcome record for the given operation.
    /// Idempotent — safe to call multiple times for the same operation_id.
    /// </summary>
    public async Task WriteOutcomeAsync(ActionOutcomeRecord record)
    {
        await _db.ExecuteDirectAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO recommendation_outcomes
                    (operation_id, action_key, action_title, executed_at, analyzed_at,
                     outcome, confidence_pct,
                     cpu_before, cpu_after, ram_before, ram_after, disk_before, disk_after,
                     insights_before, insights_after, insights_resolved,
                     stab_minutes, narrative)
                VALUES
                    (@opId, @key, @title, @execAt, @analAt,
                     @outcome, @conf,
                     @cpuB, @cpuA, @ramB, @ramA, @diskB, @diskA,
                     @insB, @insA, @insRes,
                     @stab, @narr);
                """;

            cmd.Parameters.AddWithValue("@opId",   record.OperationId);
            cmd.Parameters.AddWithValue("@key",    record.ActionKey);
            cmd.Parameters.AddWithValue("@title",  record.ActionTitle);
            cmd.Parameters.AddWithValue("@execAt", record.ExecutedAt.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("@analAt", record.AnalyzedAt.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("@outcome", record.Outcome.ToString());
            cmd.Parameters.AddWithValue("@conf",   record.ConfidencePercent);
            cmd.Parameters.AddWithValue("@cpuB",   record.CpuBefore);
            cmd.Parameters.AddWithValue("@cpuA",   record.CpuAfter);
            cmd.Parameters.AddWithValue("@ramB",   record.RamBefore);
            cmd.Parameters.AddWithValue("@ramA",   record.RamAfter);
            cmd.Parameters.AddWithValue("@diskB",  record.DiskBefore);
            cmd.Parameters.AddWithValue("@diskA",  record.DiskAfter);
            cmd.Parameters.AddWithValue("@insB",   record.InsightsActiveBefore);
            cmd.Parameters.AddWithValue("@insA",   record.InsightsActiveAfter);
            cmd.Parameters.AddWithValue("@insRes", record.InsightsResolved);
            cmd.Parameters.AddWithValue("@stab",   record.StabilizationMinutes);
            cmd.Parameters.AddWithValue("@narr",   record.NarrativeSummary);
            cmd.ExecuteNonQuery();
        }).ConfigureAwait(false);
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all outcome records, newest-first, up to <paramref name="limit"/> rows.
    /// </summary>
    public async Task<IReadOnlyList<ActionOutcomeRecord>> GetAllOutcomesAsync(int limit = 500)
    {
        return await _db.QueryAsync<IReadOnlyList<ActionOutcomeRecord>>(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT operation_id, action_key, action_title, executed_at, analyzed_at,
                       outcome, confidence_pct,
                       cpu_before, cpu_after, ram_before, ram_after, disk_before, disk_after,
                       insights_before, insights_after, insights_resolved,
                       stab_minutes, narrative
                FROM   recommendation_outcomes
                ORDER  BY executed_at DESC
                LIMIT  @limit;
                """;
            cmd.Parameters.AddWithValue("@limit", limit);
            return ReadOutcomes(cmd);
        }) ?? [];
    }

    /// <summary>
    /// Returns all operation IDs that have already been analyzed.
    /// Used by <see cref="Learning.RemediationLearningService"/> to skip
    /// re-analysis on each pass.
    /// </summary>
    public async Task<HashSet<string>> GetAnalyzedOperationIdsAsync()
    {
        return await _db.QueryAsync<HashSet<string>>(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT operation_id FROM recommendation_outcomes;";
            var ids = new HashSet<string>(StringComparer.Ordinal);
            using var r = cmd.ExecuteReader();
            while (r.Read()) ids.Add(r.GetString(0));
            return ids;
        }) ?? [];
    }

    /// <summary>Returns outcomes for a specific action key, newest-first.</summary>
    public async Task<IReadOnlyList<ActionOutcomeRecord>> GetOutcomesForActionAsync(
        string actionKey,
        int    limit = 50)
    {
        return await _db.QueryAsync<IReadOnlyList<ActionOutcomeRecord>>(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT operation_id, action_key, action_title, executed_at, analyzed_at,
                       outcome, confidence_pct,
                       cpu_before, cpu_after, ram_before, ram_after, disk_before, disk_after,
                       insights_before, insights_after, insights_resolved,
                       stab_minutes, narrative
                FROM   recommendation_outcomes
                WHERE  action_key = @key
                ORDER  BY executed_at DESC
                LIMIT  @limit;
                """;
            cmd.Parameters.AddWithValue("@key",   actionKey);
            cmd.Parameters.AddWithValue("@limit", limit);
            return ReadOutcomes(cmd);
        }) ?? [];
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static List<ActionOutcomeRecord> ReadOutcomes(SqliteCommand cmd)
    {
        var results = new List<ActionOutcomeRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var outcomeStr = reader.GetString(5);
            if (!Enum.TryParse<OutcomeClassification>(outcomeStr, out var outcome))
                outcome = OutcomeClassification.InsufficientData;

            results.Add(new ActionOutcomeRecord
            {
                OperationId          = reader.GetString(0),
                ActionKey            = reader.GetString(1),
                ActionTitle          = reader.GetString(2),
                ExecutedAt           = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)),
                AnalyzedAt           = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)),
                Outcome              = outcome,
                ConfidencePercent    = reader.GetInt32(6),
                CpuBefore            = reader.GetDouble(7),
                CpuAfter             = reader.GetDouble(8),
                RamBefore            = reader.GetDouble(9),
                RamAfter             = reader.GetDouble(10),
                DiskBefore           = reader.GetDouble(11),
                DiskAfter            = reader.GetDouble(12),
                InsightsActiveBefore = reader.GetInt32(13),
                InsightsActiveAfter  = reader.GetInt32(14),
                InsightsResolved     = reader.GetInt32(15),
                StabilizationMinutes = reader.GetInt32(16),
                NarrativeSummary     = reader.GetString(17),
            });
        }
        return results;
    }
}
