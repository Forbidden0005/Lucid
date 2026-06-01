using System.Text.Json;
using System.Text.Json.Serialization;
using Lucid.Core.Infrastructure;

namespace Lucid.Services.History;

/// <summary>
/// JSON-file-backed implementation of <see cref="IOperationHistoryService"/>.
///
/// Storage:
///   %LOCALAPPDATA%\Lucid\History\operation-history.json
///   A JSON array of <see cref="OperationRecord"/> objects, newest last.
///
/// Thread safety:
///   A <see cref="SemaphoreSlim"/> serialises every read-modify-write cycle
///   so concurrent callers never produce a torn write.
///
/// Atomic writes:
///   Content is written to a .tmp file first, then renamed over the real
///   file. If the process is killed mid-write the existing file is left
///   intact; the .tmp orphan is overwritten on the next write.
///
/// Resilience:
///   • All public methods catch and swallow every exception so a history
///     failure can never disrupt the remediation execution pipeline.
///   • A corrupted or unreadable history file is silently replaced with
///     an empty list on the next write rather than crashing the service.
///
/// Capacity:
///   Capped at <see cref="MaxRecords"/> entries. When exceeded, the oldest
///   records are evicted to keep the file small and queries fast.
/// </summary>
public sealed class OperationHistoryService : IOperationHistoryService
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>Maximum number of records to retain.</summary>
    private const int MaxRecords = 200;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly string         _filePath;
    private readonly string         _tempPath;
    private readonly SemaphoreSlim  _lock = new(1, 1);
    private readonly ILucidLogger?  _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented            = true,
        PropertyNamingPolicy     = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,   // tolerant on read for forward-compat
        DefaultIgnoreCondition   = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the history directory if absent and resolves the file paths.
    /// Safe to call during application startup on the UI thread — no I/O
    /// is performed until the first <see cref="RecordAsync"/> or
    /// <see cref="GetRecentAsync"/> call.
    /// </summary>
    public OperationHistoryService(ILucidLogger? logger = null)
    {
        _logger = logger;

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Lucid", "History");

        try { Directory.CreateDirectory(dir); }
        catch (Exception ex)
        {
            _logger?.Warning("History", $"Could not create history directory '{dir}' — history writes will fail", ex);
        }

        _filePath = Path.Combine(dir, "operation-history.json");
        _tempPath = _filePath + ".tmp";
    }

    // ── IOperationHistoryService ──────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task RecordAsync(OperationRecord record)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var records = await ReadCoreAsync().ConfigureAwait(false);
            records.Add(record);

            // Evict oldest records when the cap is exceeded.
            if (records.Count > MaxRecords)
                records.RemoveRange(0, records.Count - MaxRecords);

            await WriteCoreAsync(records).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.Warning("History", $"RecordAsync failed — operation '{record.ActionId}' not persisted", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OperationRecord>> GetRecentAsync(int limit = 50)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var records = await ReadCoreAsync().ConfigureAwait(false);
            // Records are appended chronologically; reverse for "most recent first".
            return records
                .AsEnumerable()
                .Reverse()
                .Take(limit)
                .ToList()
                .AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger?.Warning("History", "GetRecentAsync failed — returning empty list", ex);
            return [];
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task MarkRolledBackAsync(string recordId, DateTimeOffset rolledBackAt)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var records  = await ReadCoreAsync().ConfigureAwait(false);
            bool changed = false;

            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].Id != recordId) continue;
                records[i] = records[i] with
                {
                    WasRolledBack = true,
                    RolledBackAt  = rolledBackAt,
                };
                changed = true;
                break;
            }

            if (changed)
                await WriteCoreAsync(records).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.Warning("History", $"MarkRolledBackAsync failed for record '{recordId}'", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Private core I/O (always called while _lock is held) ─────────────────

    /// <summary>
    /// Reads all records from the history file.
    /// Returns an empty list if the file is absent or contains invalid JSON.
    /// </summary>
    private async Task<List<OperationRecord>> ReadCoreAsync()
    {
        if (!File.Exists(_filePath))
            return [];

        try
        {
            var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return [];

            return JsonSerializer.Deserialize<List<OperationRecord>>(json, JsonOptions)
                   ?? [];
        }
        catch (Exception ex)
        {
            _logger?.Warning("History",
                $"Operation history file '{_filePath}' is unreadable or corrupt — treating as empty. " +
                "It will be overwritten on the next successful write.", ex);
            return [];
        }
    }

    /// <summary>
    /// Atomically replaces the history file with the new record list.
    ///
    /// Writes to a .tmp file first, then renames over the real file.
    /// This ensures the existing file is never left in a partially-written
    /// state if the process is killed mid-write.
    /// </summary>
    private async Task WriteCoreAsync(List<OperationRecord> records)
    {
        var json = JsonSerializer.Serialize(records, JsonOptions);
        await File.WriteAllTextAsync(_tempPath, json).ConfigureAwait(false);

        // File.Move is a metadata-only rename on the same volume — atomic.
        File.Move(_tempPath, _filePath, overwrite: true);
    }
}
