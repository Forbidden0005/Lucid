using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Core.Infrastructure;
using Lucid.Services.Chat;
using Lucid.Services.Companion;

namespace Lucid.ViewModels;

/// <summary>
/// ViewModel for the full-page chat surface (<c>ChatPage</c>) — Lucid's home.
///
/// Composition, not duplication: the conversation itself is entirely owned by
/// <see cref="CompanionChatViewModel"/>, which this class holds and exposes as
/// <see cref="Chat"/>. What lives here is only what the page adds on top of a
/// conversation — the session rail, session lifecycle, and the avatar's state.
///
/// Persistence: the page listens for finalised messages and writes them to the
/// <see cref="IChatSessionStore"/>. A session is created lazily on the first
/// message rather than on page load, so opening Lucid and not typing anything
/// does not litter the rail with empty conversations.
///
/// Threading: constructed on the UI thread. Store calls are awaited with
/// continuations on the UI thread, since they end in ObservableCollection edits.
/// </summary>
public sealed partial class ChatWorkspaceViewModel : ObservableObject
{
    private readonly CompanionChatViewModel _chat;
    private readonly IChatSessionStore      _store;
    private readonly ILucidLogger?          _logger;
    private readonly Func<DateTimeOffset>   _clock;

    /// <summary>
    /// Tail of the persistence chain. Appends are chained rather than fired in
    /// parallel so a transcript can never be written out of order. Only ever
    /// touched on the UI thread (MessageFinalized is raised there).
    /// </summary>
    private Task _persistTail = Task.CompletedTask;

    private string? _activeSessionId;

    // ── Exposed conversation ──────────────────────────────────────────────────

    /// <summary>The conversation. All chat behaviour lives here.</summary>
    public CompanionChatViewModel Chat => _chat;

    // ── Session rail ──────────────────────────────────────────────────────────

    public ObservableCollection<ChatRailGroup> SessionGroups { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyRailVisibility))]
    private bool _hasSessions;

    /// <summary>Shown in place of the list when there is nothing to show.</summary>
    public string EmptyRailVisibility => HasSessions ? "Collapsed" : "Visible";

    /// <summary>Rail filter text. Setting it re-filters the list.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => _ = RefreshRailAsync();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RailVisibility))]
    [NotifyPropertyChangedFor(nameof(RailToggleGlyph))]
    [NotifyPropertyChangedFor(nameof(RailToggleTooltip))]
    private bool _isRailOpen = true;

    public string RailVisibility    => IsRailOpen ? "Visible" : "Collapsed";
    // Segoe MDL2 ClosePane / OpenPane — the toggle shows what the click will do.
    public string RailToggleGlyph   => IsRailOpen ? "\uE89F" : "\uE8A0";
    public string RailToggleTooltip => IsRailOpen ? "Hide conversations" : "Show conversations";

    /// <summary>Title of the open conversation, or a neutral label before one exists.</summary>
    [ObservableProperty]
    private string _activeSessionTitle = "New conversation";

    // ── Avatar ────────────────────────────────────────────────────────────────

    /// <summary>
    /// What the avatar should be doing. Derived from the conversation state today;
    /// the Listening and Speaking states are driven by the voice layer when it lands.
    /// </summary>
    [ObservableProperty]
    private string _avatarState = "Idle";

    // ── Construction ──────────────────────────────────────────────────────────

    public ChatWorkspaceViewModel(
        CompanionChatViewModel chat,
        IChatSessionStore      store,
        ILucidLogger?          logger = null,
        Func<DateTimeOffset>?  clock  = null)
    {
        _chat   = chat;
        _store  = store;
        _logger = logger;
        _clock  = clock ?? (() => DateTimeOffset.Now);

        _chat.MessageFinalized += OnMessageFinalized;
        _chat.PropertyChanged  += OnChatPropertyChanged;

        _ = RefreshRailAsync();
    }

    // No teardown method: this ViewModel and the CompanionChatViewModel it
    // subscribes to are created together and released together with the page, so
    // the handlers above can never outlive their target. The services behind them
    // (store, chat service, logger) are application-lifetime and hold no
    // reference back here.

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts a fresh conversation. The previous one is already saved — every
    /// message was written as it was finalised — so nothing is lost and no
    /// confirmation is needed.
    /// </summary>
    [RelayCommand]
    private async Task NewSessionAsync()
    {
        _chat.BeginNewSession();
        _activeSessionId    = null;
        ActiveSessionTitle  = ChatSessionTitleGenerator.DefaultTitle;
        await RefreshRailAsync();
    }

    /// <summary>Reopens a saved conversation, restoring both the transcript and the model's context.</summary>
    public async Task OpenSessionAsync(string sessionId)
    {
        try
        {
            var session = await _store.GetAsync(sessionId).ConfigureAwait(true);
            if (session is null) return;

            var transcript = await _store.LoadTranscriptAsync(sessionId).ConfigureAwait(true);

            _chat.RestoreSession(transcript);
            _activeSessionId   = sessionId;
            ActiveSessionTitle = session.Title;

            await RefreshRailAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger?.Warning("Chat", $"Could not open conversation '{sessionId}': {ex.Message}", ex);
        }
    }

    public async Task TogglePinAsync(string sessionId)
    {
        try
        {
            var session = await _store.GetAsync(sessionId).ConfigureAwait(true);
            if (session is null) return;

            await _store.SetPinnedAsync(sessionId, !session.IsPinned).ConfigureAwait(true);
            await RefreshRailAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger?.Warning("Chat", $"Could not pin conversation '{sessionId}': {ex.Message}", ex);
        }
    }

    public async Task RenameSessionAsync(string sessionId, string title)
    {
        try
        {
            await _store.RenameAsync(sessionId, title).ConfigureAwait(true);

            if (sessionId == _activeSessionId)
                ActiveSessionTitle = ChatSessionTitleGenerator.Sanitize(title);

            await RefreshRailAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger?.Warning("Chat", $"Could not rename conversation '{sessionId}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Deletes a saved conversation. When the deleted session is the one on
    /// screen, the view falls back to a fresh empty conversation rather than
    /// continuing to show a transcript that no longer exists anywhere.
    /// </summary>
    public async Task DeleteSessionAsync(string sessionId)
    {
        try
        {
            await _store.DeleteAsync(sessionId).ConfigureAwait(true);

            if (sessionId == _activeSessionId)
            {
                _chat.BeginNewSession();
                _activeSessionId   = null;
                ActiveSessionTitle = ChatSessionTitleGenerator.DefaultTitle;
            }

            await RefreshRailAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger?.Warning("Chat", $"Could not delete conversation '{sessionId}': {ex.Message}", ex);
        }
    }

    [RelayCommand]
    private void ToggleRail() => IsRailOpen = !IsRailOpen;

    // ── Persistence ───────────────────────────────────────────────────────────

    private void OnMessageFinalized(object? sender, CompanionMessage message)
    {
        var entry = ChatTranscriptMapper.ToEntry(message);

        // Chain rather than fire-and-forget: two messages finalised in quick
        // succession must reach the store in the order the user saw them.
        _persistTail = AppendChainedAsync(_persistTail, entry);
    }

    private async Task AppendChainedAsync(Task previous, ChatTranscriptEntry entry)
    {
        // Safe to await unguarded: the body below catches everything it can
        // throw, so a link in this chain never faults and one failed write can
        // never break persistence for the rest of the conversation.
        await previous.ConfigureAwait(true);

        try
        {
            var sessionId = _activeSessionId;

            if (sessionId is null)
            {
                var created      = await _store.CreateAsync().ConfigureAwait(true);
                sessionId        = created.Id;
                _activeSessionId = sessionId;
            }

            await _store.AppendAsync(sessionId, entry).ConfigureAwait(true);
            await RefreshRailAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // A failed write must never interrupt the conversation — the user
            // keeps their answer on screen, and the failure goes to diagnostics.
            _logger?.Warning("Chat", $"Could not save chat message: {ex.Message}", ex);
        }
    }

    // ── Rail refresh ──────────────────────────────────────────────────────────

    private async Task RefreshRailAsync()
    {
        try
        {
            var all = await _store.ListAsync().ConfigureAwait(true);

            HasSessions = all.Count > 0;

            var matches = ChatSessionOrganizer.Search(all, SearchText);
            var groups  = ChatSessionOrganizer.Group(matches, _clock());

            SessionGroups.Clear();
            foreach (var group in groups)
            {
                SessionGroups.Add(new ChatRailGroup(
                    group.Label,
                    group.Sessions
                         .Select(s => new ChatRailItem(s, s.Id == _activeSessionId))
                         .ToList()));
            }

            // Keep the header in step with auto-titling: the first message names
            // the session, and the header should show that name immediately.
            if (_activeSessionId is not null)
            {
                var active = all.FirstOrDefault(s => s.Id == _activeSessionId);
                if (active is not null) ActiveSessionTitle = active.Title;
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning("Chat", $"Could not refresh the conversation list: {ex.Message}", ex);
        }
    }

    // ── Avatar state ──────────────────────────────────────────────────────────

    private void OnChatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CompanionChatViewModel.IsProcessing))
            AvatarState = _chat.IsProcessing ? "Thinking" : "Idle";
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Rail item shapes
//
// These wrap the Core session records with the handful of presentation-only
// fields the rail template needs. They exist so the rail can highlight the open
// conversation and pick glyphs without converters, and so the Core records stay
// free of UI concerns.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A labelled run of sessions in the rail ("Today", "Pinned", …).</summary>
public sealed record ChatRailGroup(string Label, IReadOnlyList<ChatRailItem> Items);

/// <summary>One conversation row in the rail.</summary>
public sealed record ChatRailItem(ChatSessionSummary Session, bool IsActive)
{
    public string Id      => Session.Id;
    public string Title   => Session.Title;
    public string Preview => Session.Preview;

    public string PreviewVisibility =>
        string.IsNullOrEmpty(Session.Preview) ? "Collapsed" : "Visible";

    /// <summary>Small pin marker on the row, shown only for pinned conversations.</summary>
    public string PinIndicatorVisibility => Session.IsPinned ? "Visible" : "Collapsed";

    /// <summary>Context-menu label — the action, not the state.</summary>
    public string PinMenuLabel => Session.IsPinned ? "Unpin" : "Pin";

    /// <summary>Row fill. The open conversation is tinted rather than outlined.</summary>
    public string RowBackground => IsActive ? "#1B2338" : "Transparent";

    public string TitleColor => IsActive ? "#E4E4F0" : "#B0B0C8";
}
