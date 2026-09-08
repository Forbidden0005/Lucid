using Lucid.Services.Companion;

namespace Lucid.Services.Chat;

/// <summary>
/// Process-lifetime implementation of <see cref="IChatSessionStore"/>.
///
/// Sessions survive page navigation and companion open/close, but not an app
/// restart. This is the Phase A store: it makes the rail real (new / resume /
/// rename / pin / search all genuinely work) without prematurely committing a
/// schema to disk.
///
/// The durable implementation belongs in the SQLite persistence layer alongside
/// the other repositories, added as schema migration v2. Nothing outside this
/// class needs to change when that lands — see <see cref="IChatSessionStore"/>.
///
/// Threading: every mutation is guarded by a single lock. Appends arrive from the
/// UI thread today, but streaming completions can finalise from a thread-pool
/// continuation, so the lock is not optional.
/// </summary>
public sealed class InMemoryChatSessionStore : IChatSessionStore
{
    private readonly object                                  _gate        = new();
    private readonly Dictionary<string, ChatSessionSummary>  _sessions    = [];
    private readonly Dictionary<string, List<ChatTranscriptEntry>> _transcripts = [];

    private readonly Func<DateTimeOffset> _clock;

    /// <param name="clock">
    /// Time source. Injected so tests can produce sessions dated days apart
    /// without sleeping.
    /// </param>
    public InMemoryChatSessionStore(Func<DateTimeOffset>? clock = null)
        => _clock = clock ?? (() => DateTimeOffset.Now);

    // ── Reads ─────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<ChatSessionSummary>> ListAsync(CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(ChatSessionOrganizer.Sort(_sessions.Values));
    }

    public Task<ChatSessionSummary?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<ChatSessionSummary?>(_sessions.GetValueOrDefault(sessionId));
    }

    public Task<IReadOnlyList<ChatTranscriptEntry>> LoadTranscriptAsync(
        string            sessionId,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyList<ChatTranscriptEntry> transcript =
                _transcripts.TryGetValue(sessionId, out var entries)
                    ? entries.ToList()          // defensive copy — callers must not see later appends
                    : [];

            return Task.FromResult(transcript);
        }
    }

    // ── Writes ────────────────────────────────────────────────────────────────

    public Task<ChatSessionSummary> CreateAsync(string? title = null, CancellationToken ct = default)
    {
        var now = _clock();

        var session = new ChatSessionSummary
        {
            Id           = Guid.NewGuid().ToString("N"),
            Title        = title is null ? ChatSessionTitleGenerator.DefaultTitle
                                         : ChatSessionTitleGenerator.Sanitize(title),
            CreatedAt    = now,
            UpdatedAt    = now,
            IsAutoTitled = title is null,
        };

        lock (_gate)
        {
            _sessions[session.Id]    = session;
            _transcripts[session.Id] = [];
        }

        return Task.FromResult(session);
    }

    public Task AppendAsync(string sessionId, ChatTranscriptEntry entry, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var session)) return Task.CompletedTask;

            var transcript = _transcripts[sessionId];

            // Auto-title from the opening user message, but only while the session
            // has never been renamed by hand.
            var shouldTitle = session.IsAutoTitled
                           && entry.Role == CompanionMessageRole.User
                           && !transcript.Any(e => e.Role == CompanionMessageRole.User);

            transcript.Add(entry);

            _sessions[sessionId] = session with
            {
                Title        = shouldTitle
                                   ? ChatSessionTitleGenerator.FromFirstMessage(entry.Text)
                                   : session.Title,
                UpdatedAt    = entry.Timestamp,
                MessageCount = transcript.Count,
                Preview      = ChatSessionTitleGenerator.BuildPreview(entry.Text),
            };
        }

        return Task.CompletedTask;
    }

    public Task RenameAsync(string sessionId, string title, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var session)) return Task.CompletedTask;

            _sessions[sessionId] = session with
            {
                Title        = ChatSessionTitleGenerator.Sanitize(title),
                IsAutoTitled = false,
            };
        }

        return Task.CompletedTask;
    }

    public Task SetPinnedAsync(string sessionId, bool pinned, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
                _sessions[sessionId] = session with { IsPinned = pinned };
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _sessions.Remove(sessionId);
            _transcripts.Remove(sessionId);
        }

        return Task.CompletedTask;
    }
}
