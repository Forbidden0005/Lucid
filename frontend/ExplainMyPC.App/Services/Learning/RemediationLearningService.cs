using System.Text.Json;
using System.Text.Json.Serialization;
using ExplainMyPC.Services.History;
using ExplainMyPC.Services.Replay;

namespace ExplainMyPC.Services.Learning;

/// <summary>
/// JSON-file-backed implementation of <see cref="IRemediationLearningService"/>.
///
/// Storage:
///   %LOCALAPPDATA%\ExplainMyPC\Learning\outcome-records.json
///   A JSON array of <see cref="ActionOutcomeRecord"/> objects.
///
/// Analysis flow (AnalyzePendingActionsAsync):
///   1. Load existing outcome records (already analyzed, keyed by OperationId)
///   2. Load operation history (up to MaxHistoryFetch records)
///   3. Filter to: eligible actions not yet analyzed
///   4. Analyze up to MaxAnalysisPerPass new records (bounds resource use)
///   5. Persist any new records
///   6. Rebuild in-memory profiles via RecommendationLearningEngine
///   7. Raise ProfilesUpdated
///
/// Thread safety:
///   A SemaphoreSlim serialises every read-modify-write cycle.
///   GetProfile / GetAllProfiles are lock-free reads of an immutable snapshot.
///
/// Cold-start safety:
///   GetProfile returns null (no data) until at least one analysis pass completes.
///   ProfilesUpdated is not raised when no new records were processed.
/// </summary>
public sealed class RemediationLearningService : IRemediationLearningService
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>Maximum outcome records to retain (oldest evicted first).</summary>
    private const int MaxRecords = 500;

    /// <summary>Maximum new records to analyze per AnalyzePendingActionsAsync call.</summary>
    private const int MaxAnalysisPerPass = 10;

    /// <summary>How many operation history records to scan for pending work.</summary>
    private const int MaxHistoryFetch = 200;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly string                    _filePath;
    private readonly string                    _tempPath;
    private readonly SemaphoreSlim             _lock = new(1, 1);
    private readonly IOperationHistoryService  _operationHistory;
    private readonly IOperationalReplayService _replay;
    private readonly EffectivenessAnalyzer     _analyzer;

    /// <summary>Current in-memory profile snapshot (immutable reference swap on update).</summary>
    private volatile IReadOnlyDictionary<string, RecommendationEffectivenessProfile>
        _profiles = new Dictionary<string, RecommendationEffectivenessProfile>(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented               = true,
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Construction ──────────────────────────────────────────────────────────

    public RemediationLearningService(
        IOperationHistoryService  operationHistory,
        IOperationalReplayService replay)
    {
        _operationHistory = operationHistory;
        _replay           = replay;
        _analyzer         = new EffectivenessAnalyzer(replay);

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExplainMyPC", "Learning");

        try { Directory.CreateDirectory(dir); }
        catch { /* best-effort */ }

        _filePath = Path.Combine(dir, "outcome-records.json");
        _tempPath = _filePath + ".tmp";
    }

    // ── IRemediationLearningService ───────────────────────────────────────────

    /// <inheritdoc/>
    public RecommendationEffectivenessProfile? GetProfile(string actionKey)
    {
        _profiles.TryGetValue(actionKey, out var profile);
        return profile;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, RecommendationEffectivenessProfile> GetAllProfiles()
        => _profiles;

    /// <inheritdoc/>
    public event EventHandler? ProfilesUpdated;

    /// <inheritdoc/>
    public async Task AnalyzePendingActionsAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await AnalyzeCoreAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort — learning failures must never disrupt the UI
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Core analysis (called while _lock is held) ────────────────────────────

    private async Task AnalyzeCoreAsync()
    {
        // 1. Load existing outcome records
        var existingRecords = await ReadCoreAsync().ConfigureAwait(false);
        var analyzedIds     = new HashSet<string>(
            existingRecords.Select(r => r.OperationId),
            StringComparer.Ordinal);

        // 2. Load operation history
        var operations = await _operationHistory
            .GetRecentAsync(MaxHistoryFetch)
            .ConfigureAwait(false);

        // 3. Filter to eligible unanalyzed actions
        var pending = operations
            .Where(op => op.IsSuccess && !op.IsDryRun && !op.IsRollback)
            .Where(op => !analyzedIds.Contains(op.Id))
            .OrderBy(op => op.ExecutedAt)   // analyze oldest first
            .Take(MaxAnalysisPerPass)
            .ToList();

        if (pending.Count == 0)
            return; // nothing new to analyze

        // 4. Analyze each pending operation
        var newRecords = new List<ActionOutcomeRecord>(pending.Count);
        foreach (var op in pending)
        {
            var record = await _analyzer.AnalyzeAsync(op).ConfigureAwait(false);
            if (record is not null)
                newRecords.Add(record);
        }

        if (newRecords.Count == 0)
            return; // all were too recent or ineligible

        // 5. Merge and persist
        var merged = MergeAndCap(existingRecords, newRecords);
        await WriteCoreAsync(merged).ConfigureAwait(false);

        // 6. Rebuild profiles (pure in-memory, no I/O)
        _profiles = RecommendationLearningEngine.BuildProfiles(merged);

        // 7. Notify subscribers
        ProfilesUpdated?.Invoke(this, EventArgs.Empty);
    }

    // ── Bootstrap: load profiles on first access ──────────────────────────────

    /// <summary>
    /// Loads persisted outcome records and rebuilds profiles from them.
    /// Call once at startup to ensure profiles are available before the first
    /// AnalyzePendingActionsAsync pass completes.
    /// </summary>
    public async Task LoadPersistedProfilesAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var records = await ReadCoreAsync().ConfigureAwait(false);
            if (records.Count > 0)
                _profiles = RecommendationLearningEngine.BuildProfiles(records);
        }
        catch
        {
            // Best-effort
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Persistence helpers ───────────────────────────────────────────────────

    private async Task<List<ActionOutcomeRecord>> ReadCoreAsync()
    {
        if (!File.Exists(_filePath))
            return [];

        try
        {
            var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return [];

            return JsonSerializer.Deserialize<List<ActionOutcomeRecord>>(json, JsonOptions) ?? [];
        }
        catch
        {
            // Corrupted file — start fresh
            return [];
        }
    }

    private async Task WriteCoreAsync(List<ActionOutcomeRecord> records)
    {
        var json = JsonSerializer.Serialize(records, JsonOptions);
        await File.WriteAllTextAsync(_tempPath, json).ConfigureAwait(false);
        File.Move(_tempPath, _filePath, overwrite: true);
    }

    private static List<ActionOutcomeRecord> MergeAndCap(
        List<ActionOutcomeRecord> existing,
        List<ActionOutcomeRecord> newItems)
    {
        var merged = new List<ActionOutcomeRecord>(existing.Count + newItems.Count);
        merged.AddRange(existing);
        merged.AddRange(newItems);

        // Evict oldest when over cap
        if (merged.Count > MaxRecords)
            merged.RemoveRange(0, merged.Count - MaxRecords);

        return merged;
    }
}
