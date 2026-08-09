using System.Text.Json;

namespace Lucid.Services.Simulation;

/// <summary>
/// A lightweight snapshot of a completed simulation run.
///
/// Captured immediately after a simulation completes and persisted to
/// %LOCALAPPDATA%\Lucid\simulation_history.json.
/// JSON-serializable — all fields are primitive types.
/// </summary>
public sealed record SimulationSnapshot(
    Guid            Id,
    DateTimeOffset  CreatedAt,
    string          ScenarioLabel,
    string          HeadlineVerdict,
    string          DecisionLabel,
    string          DecisionColor,
    string          ConfidenceTierLabel,
    string          ConfidenceTierColor,
    double          RamDelta,
    double          CpuDelta,
    double          DiskDelta,
    int             ConfidencePercent,
    string          HorizonLabel);

/// <summary>Read/write contract for persisted simulation history.</summary>
public interface ISimulationHistoryService
{
    /// <summary>Returns all persisted snapshots, newest first.</summary>
    IReadOnlyList<SimulationSnapshot> GetAll();

    /// <summary>
    /// Appends <paramref name="snapshot"/> to the head of the history list
    /// and persists the updated list to disk (best-effort, never throws).
    /// </summary>
    Task SaveAsync(SimulationSnapshot snapshot);
}

/// <summary>
/// JSON-backed simulation history service.
///
/// Stores the last 20 simulation snapshots in
/// %LOCALAPPDATA%\Lucid\simulation_history.json.
///
/// Design constraints:
///   • Cold-start safe — missing or corrupt file yields empty list.
///   • Best-effort writes — a failed write is silently swallowed.
///   • Thread-safe for writes via a single SemaphoreSlim.
///   • History loaded synchronously in the constructor
///     (file read is cheap; history is only shown to the user, not critical).
/// </summary>
public sealed class SimulationHistoryService : ISimulationHistoryService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const int MaxSnapshots = 20;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lucid",
        "simulation_history.json");

    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = false };

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly List<SimulationSnapshot> _snapshots = new();
    private readonly SemaphoreSlim            _writeLock = new(1, 1);

    // ── Constructor ───────────────────────────────────────────────────────────

    public SimulationHistoryService()
    {
        LoadFromDisk();
    }

    // ── ISimulationHistoryService ─────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<SimulationSnapshot> GetAll() => _snapshots.AsReadOnly();

    /// <inheritdoc/>
    public async Task SaveAsync(SimulationSnapshot snapshot)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _snapshots.Insert(0, snapshot);               // newest at index 0
            while (_snapshots.Count > MaxSnapshots)
                _snapshots.RemoveAt(_snapshots.Count - 1);

            await PersistAsync().ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(FilePath)) return;

            var json   = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<List<SimulationSnapshot>>(json);
            if (loaded is not null)
                _snapshots.AddRange(loaded);
        }
        catch
        {
            // Cold-start safe — empty history if file is missing or corrupt
        }
    }

    private async Task PersistAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonSerializer.Serialize(_snapshots, JsonOptions);
            await File.WriteAllTextAsync(FilePath, json).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort — a failed write is not surfaced to the user
        }
    }
}
