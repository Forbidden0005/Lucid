using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplainMyPC.Services.Companion;
using ExplainMyPC.Services.Conversation;
using ExplainMyPC.Services.DesktopContext;

namespace ExplainMyPC.ViewModels;

/// <summary>
/// ViewModel for the Companion Overlay Window conversation area.
///
/// Phase 17C: Fully wired to <see cref="IOperationalConversationService"/>.
/// Queries are resolved deterministically via keyword matching and composed
/// from live platform service data. Responses include:
///   - Confidence labels
///   - Evidence items (sources consulted)
///   - Suggested navigation actions
///   - Contextual quick-action chips from desktop context
///
/// Threading: All mutations on the UI thread.
/// Lifetime:  Created by CompanionOverlayWindow; scoped to the window lifetime.
/// </summary>
public sealed partial class CompanionChatViewModel : ObservableObject
{
    // ── Max conversation history ───────────────────────────────────────────────
    private const int MaxMessages = 100;

    // ── Conversation messages ──────────────────────────────────────────────────

    /// <summary>All messages in the current session.</summary>
    public ObservableCollection<CompanionMessage> Messages { get; } = [];

    // ── Quick actions ──────────────────────────────────────────────────────────

    /// <summary>
    /// Quick-action chips. The first 6 are static; contextual suggestions
    /// are prepended dynamically when desktop context changes.
    /// </summary>
    public ObservableCollection<QuickAction> QuickActions { get; } = [];

    private static readonly IReadOnlyList<QuickAction> StaticQuickActions =
    [
        new QuickAction { Id = "investigate", Label = "Investigate",  Glyph = "",
                          Description = "Start operational investigation",
                          Query       = "Investigate root causes on my system." },
        new QuickAction { Id = "replay",      Label = "Replay",        Glyph = "",
                          Description = "Replay recent system events",
                          Query       = "What changed in the last hour?" },
        new QuickAction { Id = "cleanup",     Label = "Cleanup",       Glyph = "",
                          Description = "Find cleanup opportunities",
                          Query       = "What can I safely clean up?" },
        new QuickAction { Id = "storage",     Label = "Storage",       Glyph = "",
                          Description = "Analyze storage usage",
                          Query       = "What's using the most storage space?" },
        new QuickAction { Id = "explain",     Label = "Explain",       Glyph = "",
                          Description = "Explain current system state",
                          Query       = "Explain what my system is doing right now." },
        new QuickAction { Id = "status",      Label = "Status",        Glyph = "",
                          Description = "Overall system status",
                          Query       = "How's my PC right now?" },
    ];

    // ── Input ──────────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _inputText = string.Empty;

    // ── Processing state ───────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SendButtonVisibility))]
    private bool _isProcessing;

    public string SendButtonVisibility => IsProcessing ? "Collapsed" : "Visible";
    public string SpinnerVisibility    => IsProcessing ? "Visible"   : "Collapsed";

    // ── Desktop context awareness ─────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContextBannerVisibility))]
    private string _contextBannerText = string.Empty;

    [ObservableProperty]
    private string _contextGlyph = "";   // Segoe MDL2 glyph

    public string ContextBannerVisibility =>
        string.IsNullOrEmpty(ContextBannerText) ? "Collapsed" : "Visible";

    // ── Overlay state ──────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BubbleVisibility))]
    [NotifyPropertyChangedFor(nameof(ExpandedVisibility))]
    private CompanionOverlayState _overlayState = CompanionOverlayState.Bubble;

    public string BubbleVisibility   =>
        OverlayState == CompanionOverlayState.Bubble   ? "Visible" : "Collapsed";
    public string ExpandedVisibility =>
        OverlayState == CompanionOverlayState.Expanded ? "Visible" : "Collapsed";
    public string NoMessagesVisibility =>
        Messages.Count == 0 ? "Visible" : "Collapsed";

    // ── Constructor ────────────────────────────────────────────────────────────

    public CompanionChatViewModel()
    {
        // Populate static quick actions
        foreach (var qa in StaticQuickActions)
            QuickActions.Add(qa);

        AddWelcomeMessages();
    }

    // ── Contextual suggestion refresh ──────────────────────────────────────────

    /// <summary>
    /// Called by the window whenever the desktop context changes.
    /// Replaces any existing contextual chips with fresh suggestions from the engine.
    /// </summary>
    public void RefreshContextualSuggestions()
    {
        try
        {
            var suggestions = AppServices.ConversationService.GetContextualSuggestions();

            // Remove existing contextual chips
            for (var i = QuickActions.Count - 1; i >= 0; i--)
            {
                if (QuickActions[i].IsContextual)
                    QuickActions.RemoveAt(i);
            }

            // Prepend new contextual chips (max 2 to avoid overwhelming the strip)
            for (var i = Math.Min(suggestions.Count, 2) - 1; i >= 0; i--)
                QuickActions.Insert(0, ContextualSuggestionEngine.ToQuickAction(suggestions[i]));
        }
        catch
        {
            // Suggestion refresh is best-effort; never surface an error to the user
        }
    }

    // ── Welcome messages ───────────────────────────────────────────────────────

    private void AddWelcomeMessages()
    {
        Messages.Add(new CompanionMessage
        {
            Id        = "welcome-1",
            Role      = CompanionMessageRole.System,
            Text      = "Hi — I'm your Operational Companion. Ask me anything about your system.",
            Timestamp = DateTimeOffset.Now,
            Category  = CompanionMessageCategory.Welcome,
        });

        Messages.Add(new CompanionMessage
        {
            Id        = "welcome-2",
            Role      = CompanionMessageRole.System,
            Text      = "All analysis runs locally on this device. Nothing leaves your machine.",
            Timestamp = DateTimeOffset.Now,
            Category  = CompanionMessageCategory.Answer,
        });
    }

    // ── Commands ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends the current InputText as a user message and gets a real
    /// operational response from the conversation service.
    /// </summary>
    [RelayCommand]
    private async Task SendMessageAsync()
    {
        var text = InputText.Trim();
        if (string.IsNullOrEmpty(text) || IsProcessing) return;

        // Add user message immediately
        AddUserMessage(text);
        InputText  = string.Empty;
        IsProcessing = true;
        OnPropertyChanged(nameof(NoMessagesVisibility));

        try
        {
            var snap = AppServices.DesktopContext.CurrentSnapshot;
            var prompt = new OperationalPrompt
            {
                Text               = text,
                DesktopContextFocus = snap?.CurrentOperationalFocus,
                ActiveAppCategory  = snap?.ActiveWindow?.AppCategory.ToString(),
                ClipboardHasFiles  = snap?.ClipboardContext?.HasFiles ?? false,
            };

            var response = await AppServices.ConversationService
                .ProcessQueryAsync(prompt)
                .ConfigureAwait(true); // stay on UI thread

            var message = OperationalConversationService.ResponseToMessage(response);
            EnqueueMessage(message);
        }
        catch (Exception ex)
        {
            EnqueueMessage(new CompanionMessage
            {
                Id        = Guid.NewGuid().ToString("N")[..8],
                Role      = CompanionMessageRole.System,
                Text      = $"Analysis encountered an error: {ex.Message}",
                Timestamp = DateTimeOffset.Now,
                Category  = CompanionMessageCategory.Error,
            });
        }
        finally
        {
            IsProcessing = false;
            OnPropertyChanged(nameof(NoMessagesVisibility));
        }
    }

    // Synchronous alias — used by code-behind only; NOT a RelayCommand.
    // XAML binds to SendMessageCommand which [RelayCommand] on SendMessageAsync generates.
    private void SendMessage() => _ = SendMessageAsync();

    /// <summary>
    /// Submits a quick-action or contextual suggestion query as a user message.
    /// </summary>
    [RelayCommand]
    private void ExecuteQuickAction(QuickAction action)
    {
        if (action is null) return;
        InputText = action.Query;
        _ = SendMessageAsync();
    }

    /// <summary>Clears all messages and restores the welcome state.</summary>
    [RelayCommand]
    private void ClearConversation()
    {
        Messages.Clear();
        AddWelcomeMessages();
        OnPropertyChanged(nameof(NoMessagesVisibility));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void AddUserMessage(string text)
    {
        EnqueueMessage(new CompanionMessage
        {
            Id        = Guid.NewGuid().ToString("N")[..8],
            Role      = CompanionMessageRole.User,
            Text      = text,
            Timestamp = DateTimeOffset.Now,
            Category  = CompanionMessageCategory.Answer,
        });
    }

    private void EnqueueMessage(CompanionMessage message)
    {
        // Trim oldest messages if we exceed the cap (keeps memory bounded)
        while (Messages.Count >= MaxMessages)
            Messages.RemoveAt(0);

        Messages.Add(message);
        OnPropertyChanged(nameof(NoMessagesVisibility));
    }
}
