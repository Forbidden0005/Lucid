using System.Text.Json;

namespace ExplainMyPC.Services.Learning;

// ── Interface ─────────────────────────────────────────────────────────────────

/// <summary>
/// Persists and retrieves user interaction records (accept / dismiss) for
/// recommended actions.  Used by PersonalizationEngine to build the local
/// behavioral profile.
///
/// All storage is local-only (%LOCALAPPDATA%\ExplainMyPC\intervention_memory.json).
/// No cloud, no telemetry upload, no personal identity inference.
/// </summary>
public interface IInterventionMemoryService
{
    /// <summary>All records currently loaded, newest-first.</summary>
    IReadOnlyList<InterventionRecord> Records { get; }

    /// <summary>
    /// Records that the user accepted (executed) an action.
    /// Fire-and-forget — failures are swallowed and never surface to the UI.
    /// </summary>
    void RecordAcceptance(string actionKey, string actionTitle, string category, string insightSeverity);

    /// <summary>
    /// Records that the user dismissed (ignored) an action.
    /// </summary>
    void RecordDismissal(string actionKey, string actionTitle, string category, string insightSeverity);

    /// <summary>
    /// Returns the most recent <paramref name="count"/> records across all categories.
    /// </summary>
    IReadOnlyList<InterventionRecord> GetRecent(int count = 20);

    /// <summary>
    /// Returns all records for the specified category key (e.g. "disk", "startup").
    /// </summary>
    IReadOnlyList<InterventionRecord> GetByCategory(string category);

    /// <summary>
    /// Returns how many times the action has been shown recently (within last 7 days).
    /// Used by AlertFatigueManager to compute suppression factors.
    /// </summary>
    int GetRecentShowCount(string actionKey, int withinDays = 7);
}

// ── Implementation ─────────────────────────────────────────────────────────────

/// <summary>
/// JSON-file-backed implementation of IInterventionMemoryService.
/// Keeps the last 200 records in memory and persists them to
/// %LOCALAPPDATA%\ExplainMyPC\intervention_memory.json on every write.
///
/// Thread safety: all public members are safe to call from the UI thread.
/// File I/O is dispatched to the thread pool via fire-and-forget Tasks.
/// </summary>
public sealed class InterventionMemoryService : IInterventionMemoryService
{
    private const int MaxRecords = 200;

    private static readonly string s_filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ExplainMyPC", "intervention_memory.json");

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly List<InterventionRecord> _records = [];

    // ── Constructor ───────────────────────────────────────────────────────────

    public InterventionMemoryService()
    {
        _ = LoadAsync();
    }

    // ── IInterventionMemoryService ────────────────────────────────────────────

    public IReadOnlyList<InterventionRecord> Records => _records.AsReadOnly();

    public void RecordAcceptance(
        string actionKey, string actionTitle, string category, string insightSeverity) =>
        AddRecord(actionKey, actionTitle, category, insightSeverity, accepted: true);

    public void RecordDismissal(
        string actionKey, string actionTitle, string category, string insightSeverity) =>
        AddRecord(actionKey, actionTitle, category, insightSeverity, accepted: false);

    public IReadOnlyList<InterventionRecord> GetRecent(int count = 20) =>
        _records.Take(count).ToList().AsReadOnly();

    public IReadOnlyList<InterventionRecord> GetByCategory(string category) =>
        _records
            .Where(r => string.Equals(r.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToList()
            .AsReadOnly();

    public int GetRecentShowCount(string actionKey, int withinDays = 7)
    {
        var cutoff = DateTimeOffset.Now.AddDays(-withinDays);
        return _records.Count(r =>
            string.Equals(r.ActionKey, actionKey, StringComparison.OrdinalIgnoreCase) &&
            r.ShownAt >= cutoff);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void AddRecord(
        string actionKey, string actionTitle, string category,
        string insightSeverity, bool accepted)
    {
        var record = new InterventionRecord
        {
            Id             = Guid.NewGuid().ToString("N"),
            ActionKey      = actionKey,
            ActionTitle    = actionTitle,
            Category       = category,
            InsightSeverity = insightSeverity,
            ShownAt        = DateTimeOffset.Now,
            Accepted       = accepted,
        };

        // Prepend newest-first; cap at MaxRecords
        _records.Insert(0, record);
        if (_records.Count > MaxRecords)
            _records.RemoveRange(MaxRecords, _records.Count - MaxRecords);

        _ = PersistAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(s_filePath)) return;

            await using var stream = File.OpenRead(s_filePath);
            var loaded = await JsonSerializer.DeserializeAsync<List<InterventionRecord>>(
                stream, s_jsonOptions).ConfigureAwait(false);

            if (loaded is { Count: > 0 })
            {
                _records.Clear();
                _records.AddRange(loaded.Take(MaxRecords));
            }
        }
        catch
        {
            // Best-effort — fresh start if file is corrupted or missing
        }
    }

    private async Task PersistAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(s_filePath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var tmp = s_filePath + ".tmp";
            await using (var stream = File.Create(tmp))
                await JsonSerializer.SerializeAsync(stream, _records, s_jsonOptions)
                    .ConfigureAwait(false);

            File.Move(tmp, s_filePath, overwrite: true);
        }
        catch
        {
            // Best-effort — failure never surfaces to the UI
        }
    }
}
