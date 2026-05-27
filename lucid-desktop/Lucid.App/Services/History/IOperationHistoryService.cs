namespace Lucid.Services.History;

/// <summary>
/// Provides durable persistence and retrieval of remediation operation history.
///
/// All methods are async-safe: concurrent callers are serialised internally
/// so the caller does not need to coordinate access.
///
/// History recording is best-effort — implementations must not propagate
/// exceptions to callers. A failed write should be silently swallowed so
/// that history persistence never disrupts the remediation execution flow.
///
/// Current implementation: lightweight JSON file at
///   %LOCALAPPDATA%\Lucid\History\operation-history.json
/// Future implementation: SQLite when query requirements grow.
/// </summary>
public interface IOperationHistoryService
{
    /// <summary>
    /// Appends <paramref name="record"/> to the persistent history log.
    ///
    /// Implementations cap total stored records and evict the oldest entries
    /// when the cap is exceeded.
    ///
    /// Must not throw — all write failures are swallowed internally.
    /// </summary>
    Task RecordAsync(OperationRecord record);

    /// <summary>
    /// Returns up to <paramref name="limit"/> records ordered from most
    /// recent to oldest.
    ///
    /// Returns an empty list if the history file is absent or unreadable.
    /// </summary>
    Task<IReadOnlyList<OperationRecord>> GetRecentAsync(int limit = 50);

    /// <summary>
    /// Marks an existing record as rolled back by updating its
    /// <see cref="OperationRecord.WasRolledBack"/> and
    /// <see cref="OperationRecord.RolledBackAt"/> fields.
    ///
    /// No-ops silently when <paramref name="recordId"/> is not found.
    /// Must not throw.
    /// </summary>
    Task MarkRolledBackAsync(string recordId, DateTimeOffset rolledBackAt);
}
