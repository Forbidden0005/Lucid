using ExplainMyPC.Services.History;
using ExplainMyPC.Services.Persistence;
using ExplainMyPC.Services.Replay;

namespace ExplainMyPC.Services.Learning;

/// <summary>
/// SQLite-backed implementation of <see cref="IRemediationLearningService"/>.
///
/// Storage delegate:
///   All outcome records are persisted via <see cref="RecommendationOutcomeRepository"/>
///   (INSERT OR REPLACE, idempotent per operation_id).
///   Replaces the previous JSON-file approach.
///
/// Analysis flow (AnalyzePendingActionsAsync):
///   1. Load already-analyzed operation IDs from the repository
///   2. Load operation history (up to MaxHistoryFetch records)
///   3. Filter to: eligible actions not yet analyzed
///   4. Analyze up to MaxAnalysisPerPass new records (bounds resource use)
///   5. Write each new record directly to the repository
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

    /// <summary>Maximum outcome records returned when rebuilding profiles.</summary>
    private const int MaxRecords = 500;

    /// <summary>Maximum new records to analyze per AnalyzePendingActionsAsync call.</summary>
    private const int MaxAnalysisPerPass = 10;

    /// <summary>How many operation history records to scan for pending work.</summary>
    private const int MaxHistoryFetch = 200;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly SemaphoreSlim                   _lock = new(1, 1);
    private readonly IOperationHistoryService        _operationHistory;
    private readonly IOperationalReplayService       _replay;
    private readonly RecommendationOutcomeRepository _outcomeRepo;
    private readonly EffectivenessAnalyzer           _analyzer;

    /// <summary>Current in-memory profile snapshot (immutable reference swap on update).</summary>
    private volatile IReadOnlyDictionary<string, RecommendationEffectivenessProfile>
        _profiles = new Dictionary<string, RecommendationEffectivenessProfile>(StringComparer.Ordinal);

    // ── Construction ──────────────────────────────────────────────────────────

    public RemediationLearningService(
        IOperationHistoryService        operationHistory,
        IOperationalReplayService       replay,
        RecommendationOutcomeRepository outcomeRepo)
    {
        _operationHistory = operationHistory;
        _replay           = replay;
        _outcomeRepo      = outcomeRepo;
        _analyzer         = new EffectivenessAnalyzer(replay);
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
        // 1. Load IDs of operations already analyzed so we skip them
        var analyzedIds = await _outcomeRepo
            .GetAnalyzedOperationIdsAsync()
            .ConfigureAwait(false);

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
        int written = 0;
        foreach (var op in pending)
        {
            var record = await _analyzer.AnalyzeAsync(op).ConfigureAwait(false);
            if (record is null) continue;   // too recent or ineligible

            await _outcomeRepo.WriteOutcomeAsync(record).ConfigureAwait(false);
            written++;
        }

        if (written == 0)
            return; // all were too recent

        // 5. Rebuild profiles from the full stored history
        var allOutcomes = await _outcomeRepo
            .GetAllOutcomesAsync(MaxRecords)
            .ConfigureAwait(false);

        _profiles = RecommendationLearningEngine.BuildProfiles(allOutcomes);

        // 6. Notify subscribers
        ProfilesUpdated?.Invoke(this, EventArgs.Empty);
    }

    // ── Bootstrap: load profiles on first access ──────────────────────────────

    /// <summary>
    /// Loads persisted outcome records from SQLite and rebuilds profiles from them.
    /// Call once at startup to ensure profiles are available before the first
    /// AnalyzePendingActionsAsync pass completes.
    /// </summary>
    public async Task LoadPersistedProfilesAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var records = await _outcomeRepo
                .GetAllOutcomesAsync(MaxRecords)
                .ConfigureAwait(false);

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
}
