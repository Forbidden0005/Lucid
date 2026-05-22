namespace ExplainMyPC.Services.Companion;

/// <summary>
/// In-memory companion session manager.
///
/// Retains messages for the lifetime of the current app session only.
/// Conversations are ephemeral — nothing is written to disk.
/// This preserves user privacy and keeps the companion lightweight.
/// </summary>
public sealed class CompanionSessionManager : ICompanionSessionManager
{
    private readonly List<CompanionMessage> _messages = [];

    public IReadOnlyList<CompanionMessage> Messages => _messages;

    public event EventHandler<CompanionMessage>? MessageAdded;

    // ── Message creation ───────────────────────────────────────────────────────

    public void AddUserMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var msg = new CompanionMessage
        {
            Id        = Guid.NewGuid().ToString("N")[..8],
            Role      = CompanionMessageRole.User,
            Text      = text.Trim(),
            Timestamp = DateTimeOffset.Now,
            Category  = CompanionMessageCategory.Answer,
        };

        _messages.Add(msg);
        MessageAdded?.Invoke(this, msg);
    }

    public void AddSystemMessage(
        string                   text,
        CompanionMessageCategory category = CompanionMessageCategory.Answer,
        string?                  actionId = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var msg = new CompanionMessage
        {
            Id        = Guid.NewGuid().ToString("N")[..8],
            Role      = CompanionMessageRole.System,
            Text      = text.Trim(),
            Timestamp = DateTimeOffset.Now,
            Category  = category,
            ActionId  = actionId,
        };

        _messages.Add(msg);
        MessageAdded?.Invoke(this, msg);
    }

    public void ClearSession() => _messages.Clear();

    public CompanionMessage BuildWelcomeMessage() => new()
    {
        Id        = "welcome",
        Role      = CompanionMessageRole.System,
        Text      = "I'm your operational companion. Ask me about performance, " +
                    "memory, disk usage, or what changed on your system. " +
                    "All analysis uses real telemetry from this machine — nothing leaves this device.",
        Timestamp = DateTimeOffset.Now,
        Category  = CompanionMessageCategory.Welcome,
    };
}
