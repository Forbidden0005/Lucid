namespace Lucid.Services.History;

/// <summary>
/// Immutable record of a single remediation action execution or rollback
/// persisted to the operation history log.
///
/// Design:
///   • Declared as a C# <c>record</c> so callers can produce modified
///     copies with <see langword="with"/> expressions (used by
///     <see cref="IOperationHistoryService.MarkRolledBackAsync"/>).
///   • All properties use <see langword="init"/>-only setters so the
///     record is fully populated at construction and System.Text.Json
///     can deserialise it correctly in .NET 8.
///   • Collections are <see cref="List{T}"/> (not <c>IReadOnlyList</c>)
///     to ensure System.Text.Json round-trips them without a custom
///     converter.
///
/// Duration:
///   Stored as <see cref="DurationMs"/> (milliseconds, <see langword="long"/>)
///   rather than <see cref="TimeSpan"/> to avoid System.Text.Json's
///   verbose object-based TimeSpan serialisation.
///
/// Warnings / Errors:
///   Only Warning- and Error-level log entries are stored here. The full
///   per-file log is transient (shown live in the action card) and is not
///   persisted — only the count is kept for audit purposes.
/// </summary>
public sealed record OperationRecord
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Stable unique identifier for this record, generated at creation time.
    /// Format: 32-character lowercase hex (no hyphens).
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Dot-separated action identifier, e.g. "action.disk.clean-temp-files".</summary>
    public string ActionId { get; init; } = string.Empty;

    /// <summary>Human-readable action title, e.g. "Clean Temporary Files".</summary>
    public string ActionTitle { get; init; } = string.Empty;

    // ── Execution timing ──────────────────────────────────────────────────────

    /// <summary>Moment the execution or rollback attempt began.</summary>
    public DateTimeOffset ExecutedAt { get; init; }

    /// <summary>Wall-clock duration of the attempt, in milliseconds.</summary>
    public long DurationMs { get; init; }

    // ── Outcome ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Terminal status as a string, e.g. "Success", "Failed", "Cancelled".
    /// Stored as a string rather than the enum so the JSON is human-readable
    /// and forward-compatible with future status additions.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// <see langword="true"/> for Success and PartialSuccess statuses only.
    /// Dry-run results are not considered applied; check <see cref="IsDryRun"/>.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary><see langword="true"/> when the action was simulated but not applied.</summary>
    public bool IsDryRun { get; init; }

    /// <summary><see langword="true"/> when this record represents a rollback rather than a forward execution.</summary>
    public bool IsRollback { get; init; }

    /// <summary>Short outcome message as returned by the executor.</summary>
    public string Message { get; init; } = string.Empty;

    // ── Rollback metadata ─────────────────────────────────────────────────────

    /// <summary>
    /// <see langword="true"/> when the execution left a rollback snapshot
    /// that the user can use to undo the action.
    /// </summary>
    public bool CanRollback { get; init; }

    /// <summary>
    /// Opaque executor-defined rollback snapshot identifier.
    /// <see langword="null"/> when rollback is not supported for this execution.
    /// </summary>
    public string? RollbackToken { get; init; }

    /// <summary>
    /// <see langword="true"/> when the action was subsequently rolled back.
    /// Set by <see cref="IOperationHistoryService.MarkRolledBackAsync"/>.
    /// </summary>
    public bool WasRolledBack { get; init; }

    /// <summary>
    /// Timestamp of the rollback, if one was performed.
    /// <see langword="null"/> when <see cref="WasRolledBack"/> is <see langword="false"/>.
    /// </summary>
    public DateTimeOffset? RolledBackAt { get; init; }

    // ── Log summary ───────────────────────────────────────────────────────────

    /// <summary>
    /// Total number of log entries emitted during the execution.
    /// The full per-file log is transient and not persisted; this count
    /// is kept for audit and display purposes.
    /// </summary>
    public int TotalLogEntries { get; init; }

    /// <summary>
    /// Warning-level log messages from the execution.
    /// Typically: files skipped due to lock or access-denied conditions.
    /// </summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// Error-level log messages from the execution.
    /// Non-empty only when the executor encountered a hard failure.
    /// </summary>
    public List<string> Errors { get; init; } = [];
}
