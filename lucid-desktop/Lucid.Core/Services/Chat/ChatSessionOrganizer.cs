namespace Lucid.Services.Chat;

/// <summary>
/// Pure ordering, grouping and filtering rules for the session rail.
///
/// Kept as a stateless helper rather than living inside the store so the rail's
/// behaviour is testable without a database and stays identical no matter which
/// <see cref="IChatSessionStore"/> implementation is behind it.
///
/// "Now" is always passed in rather than read from the clock — date bucketing is
/// the kind of logic that quietly breaks around midnight and across DST, and an
/// injected clock is the only way to test those cases.
/// </summary>
public static class ChatSessionOrganizer
{
    /// <summary>
    /// Rail order: pinned sessions first, then most-recently-updated first.
    /// Title is the final tiebreaker so the order is stable when two sessions
    /// share a timestamp (which happens on bulk import and in tests).
    /// </summary>
    public static IReadOnlyList<ChatSessionSummary> Sort(IEnumerable<ChatSessionSummary> sessions) =>
        sessions
            .OrderByDescending(s => s.IsPinned)
            .ThenByDescending(s => s.UpdatedAt)
            .ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Case-insensitive substring match over title and preview.
    /// An empty or whitespace-only query matches everything.
    /// </summary>
    public static IReadOnlyList<ChatSessionSummary> Search(
        IEnumerable<ChatSessionSummary> sessions,
        string?                         query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return sessions as IReadOnlyList<ChatSessionSummary> ?? sessions.ToList();

        var q = query.Trim();

        return sessions
            .Where(s => s.Title.Contains(q,   StringComparison.OrdinalIgnoreCase)
                     || s.Preview.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Buckets sessions into the rail's labelled groups, sorted within each group.
    /// Empty groups are omitted, so the rail never renders a header with nothing
    /// under it. Pinned sessions appear only in the Pinned group.
    /// </summary>
    public static IReadOnlyList<ChatSessionGroup> Group(
        IEnumerable<ChatSessionSummary> sessions,
        DateTimeOffset                  now)
    {
        var sorted = Sort(sessions);
        var today  = now.LocalDateTime.Date;

        var buckets = new Dictionary<ChatSessionGroupKind, List<ChatSessionSummary>>();

        foreach (var session in sorted)
        {
            var kind = ClassifyInternal(session, today);
            if (!buckets.TryGetValue(kind, out var list))
                buckets[kind] = list = [];
            list.Add(session);
        }

        // Enum order is render order.
        return Enum.GetValues<ChatSessionGroupKind>()
            .Where(buckets.ContainsKey)
            .Select(kind => new ChatSessionGroup
            {
                Kind     = kind,
                Label    = LabelFor(kind),
                Sessions = buckets[kind],
            })
            .ToList();
    }

    /// <summary>
    /// Which rail group a single session belongs to. Exposed for tests and for
    /// callers that need to place one session without regrouping the whole rail.
    /// </summary>
    public static ChatSessionGroupKind Classify(ChatSessionSummary session, DateTimeOffset now) =>
        ClassifyInternal(session, now.LocalDateTime.Date);

    public static string LabelFor(ChatSessionGroupKind kind) => kind switch
    {
        ChatSessionGroupKind.Pinned        => "Pinned",
        ChatSessionGroupKind.Today         => "Today",
        ChatSessionGroupKind.Yesterday     => "Yesterday",
        ChatSessionGroupKind.PreviousWeek  => "Previous 7 days",
        ChatSessionGroupKind.PreviousMonth => "Previous 30 days",
        _                                  => "Older",
    };

    // ── Internals ─────────────────────────────────────────────────────────────

    private static ChatSessionGroupKind ClassifyInternal(ChatSessionSummary session, DateTime today)
    {
        if (session.IsPinned) return ChatSessionGroupKind.Pinned;

        // Compare calendar days, not elapsed hours: a conversation at 11pm last
        // night is "Yesterday" at 1am, not "Today minus two hours".
        var days = (today - session.UpdatedAt.LocalDateTime.Date).Days;

        return days switch
        {
            <= 0  => ChatSessionGroupKind.Today,        // future timestamps clamp to Today
            1     => ChatSessionGroupKind.Yesterday,
            <= 7  => ChatSessionGroupKind.PreviousWeek,
            <= 30 => ChatSessionGroupKind.PreviousMonth,
            _     => ChatSessionGroupKind.Older,
        };
    }
}
