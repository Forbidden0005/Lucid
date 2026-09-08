namespace Lucid.Services.Chat;

/// <summary>
/// Storage for saved conversations and their transcripts.
///
/// Every method is asynchronous even though the current in-memory implementation
/// completes synchronously. That is deliberate: the SQLite-backed implementation
/// is the intended production store, and having callers already written against
/// an async contract means adopting it is a registration change rather than a
/// rewrite of every call site.
///
/// Implementations must be safe to call from the UI thread and must never throw
/// for an unknown session id — a session the user just deleted can still be the
/// target of an in-flight append, and that is not an error worth surfacing.
/// </summary>
public interface IChatSessionStore
{
    /// <summary>
    /// All sessions in rail order (pinned first, then most recently updated).
    /// </summary>
    Task<IReadOnlyList<ChatSessionSummary>> ListAsync(CancellationToken ct = default);

    /// <summary>Returns a single session, or null when it does not exist.</summary>
    Task<ChatSessionSummary?> GetAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Creates an empty session and returns it. Passing a title marks the session
    /// as user-titled; omitting it leaves the session auto-titled, so the first
    /// user message will name it.
    /// </summary>
    Task<ChatSessionSummary> CreateAsync(string? title = null, CancellationToken ct = default);

    /// <summary>
    /// The full transcript in chronological order. Returns an empty list for an
    /// unknown session.
    /// </summary>
    Task<IReadOnlyList<ChatTranscriptEntry>> LoadTranscriptAsync(
        string            sessionId,
        CancellationToken ct = default);

    /// <summary>
    /// Appends one message and refreshes the session's UpdatedAt, MessageCount and
    /// Preview. When the session is still auto-titled and this is its first user
    /// message, the title is derived from it. No-ops for an unknown session.
    /// </summary>
    Task AppendAsync(string sessionId, ChatTranscriptEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Renames a session and marks it user-titled so auto-titling never overwrites
    /// the new name. No-ops for an unknown session.
    /// </summary>
    Task RenameAsync(string sessionId, string title, CancellationToken ct = default);

    /// <summary>Pins or unpins a session. No-ops for an unknown session.</summary>
    Task SetPinnedAsync(string sessionId, bool pinned, CancellationToken ct = default);

    /// <summary>
    /// Permanently removes a session and its transcript. No-ops for an unknown
    /// session so a double-delete is harmless.
    /// </summary>
    Task DeleteAsync(string sessionId, CancellationToken ct = default);
}
