using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplainMyPC.Services.Companion;

namespace ExplainMyPC.ViewModels;

/// <summary>
/// ViewModel for the Companion Overlay Window conversation area.
///
/// Phase 17A: Placeholder implementation.
/// Exposes two welcome messages and local echo for user input.
/// NO real intelligence wiring — OperationalConversationEngine is
/// intentionally not connected in this phase.
///
/// Threading: all mutations on the UI thread.
/// Lifetime:  created by CompanionOverlayWindow; scoped to the window lifetime.
/// </summary>
public sealed partial class CompanionChatViewModel : ObservableObject
{
    // ── Conversation messages ──────────────────────────────────────────────────

    /// <summary>All messages in the current session.</summary>
    public ObservableCollection<CompanionMessage> Messages { get; } = [];

    // ── Quick actions ──────────────────────────────────────────────────────────

    /// <summary>
    /// Predefined quick-action chips shown below the message list.
    /// Each chip submits a natural-language query stub; full analysis
    /// requires the intelligence layer to be wired (future phase).
    /// </summary>
    public IReadOnlyList<QuickAction> QuickActions { get; } =
    [
        new QuickAction { Id = "investigate", Label = "Investigate",  Glyph = "",
                          Description = "Start operational investigation",
                          Query       = "What's affecting my system right now?" },
        new QuickAction { Id = "replay",      Label = "Replay",        Glyph = "",
                          Description = "Replay recent system events",
                          Query       = "What changed in the last hour?" },
        new QuickAction { Id = "cleanup",     Label = "Cleanup",       Glyph = "",
                          Description = "Find cleanup opportunities",
                          Query       = "What can I safely clean up?" },
        new QuickAction { Id = "repairs",     Label = "Repairs",       Glyph = "",
                          Description = "Check for repair recommendations",
                          Query       = "Are there any system repairs recommended?" },
        new QuickAction { Id = "storage",     Label = "Storage",       Glyph = "",
                          Description = "Analyze storage usage",
                          Query       = "What's using the most storage space?" },
        new QuickAction { Id = "explain",     Label = "Explain",       Glyph = "",
                          Description = "Explain current system state",
                          Query       = "Explain what my system is doing right now." },
    ];

    // ── Input ──────────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _inputText = string.Empty;

    // ── Overlay state (driven by ICompanionSessionManager via the window) ──────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BubbleVisibility))]
    [NotifyPropertyChangedFor(nameof(ExpandedVisibility))]
    private CompanionOverlayState _overlayState = CompanionOverlayState.Bubble;

    /// <summary>Visibility string for the bubble layout.</summary>
    public string BubbleVisibility   => OverlayState == CompanionOverlayState.Bubble   ? "Visible" : "Collapsed";

    /// <summary>Visibility string for the expanded conversation panel layout.</summary>
    public string ExpandedVisibility => OverlayState == CompanionOverlayState.Expanded ? "Visible" : "Collapsed";

    /// <summary>Visibility string for the empty-state prompt.</summary>
    public string NoMessagesVisibility => Messages.Count == 0 ? "Visible" : "Collapsed";

    // ── Constructor ────────────────────────────────────────────────────────────

    public CompanionChatViewModel()
    {
        AddWelcomeMessages();
    }

    // ── Welcome messages ───────────────────────────────────────────────────────

    private void AddWelcomeMessages()
    {
        Messages.Add(new CompanionMessage
        {
            Id        = "welcome-1",
            Role      = CompanionMessageRole.System,
            Text      = "Hi — I'm your Operational Companion. I can help you understand what your system is doing, surface insights, and explain what's changed.",
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
    /// Sends the current InputText as a user message and adds a placeholder response.
    /// Phase 17A: no intelligence engine wired.
    /// </summary>
    [RelayCommand]
    private void SendMessage()
    {
        var text = InputText.Trim();
        if (string.IsNullOrEmpty(text)) return;

        Messages.Add(new CompanionMessage
        {
            Id        = Guid.NewGuid().ToString("N")[..8],
            Role      = CompanionMessageRole.User,
            Text      = text,
            Timestamp = DateTimeOffset.Now,
            Category  = CompanionMessageCategory.Answer,
        });

        InputText = string.Empty;

        // Placeholder response — intelligence layer connects in Phase 17B
        Messages.Add(new CompanionMessage
        {
            Id        = Guid.NewGuid().ToString("N")[..8],
            Role      = CompanionMessageRole.System,
            Text      = "I received your message. Full operational analysis connects in the next phase. For live data, check the Dashboard, Insights, and Timeline pages.",
            Timestamp = DateTimeOffset.Now,
            Category  = CompanionMessageCategory.Answer,
        });

        OnPropertyChanged(nameof(NoMessagesVisibility));
    }

    /// <summary>
    /// Submits a quick-action query stub as a user message.
    /// Phase 17A: placeholder response only.
    /// </summary>
    [RelayCommand]
    private void ExecuteQuickAction(QuickAction action)
    {
        if (action is null) return;

        Messages.Add(new CompanionMessage
        {
            Id        = Guid.NewGuid().ToString("N")[..8],
            Role      = CompanionMessageRole.User,
            Text      = action.Query,
            Timestamp = DateTimeOffset.Now,
            Category  = CompanionMessageCategory.Answer,
        });

        Messages.Add(new CompanionMessage
        {
            Id        = Guid.NewGuid().ToString("N")[..8],
            Role      = CompanionMessageRole.System,
            Text      = $"The \"{action.Label}\" analysis connects when the intelligence layer is wired. Use the sidebar to navigate to the relevant page for current data.",
            Timestamp = DateTimeOffset.Now,
            Category  = CompanionMessageCategory.Answer,
        });

        OnPropertyChanged(nameof(NoMessagesVisibility));
    }

    /// <summary>Clears all messages and restores the welcome state.</summary>
    [RelayCommand]
    private void ClearConversation()
    {
        Messages.Clear();
        AddWelcomeMessages();
        OnPropertyChanged(nameof(NoMessagesVisibility));
    }
}
