namespace ExplainMyPC.Services.Companion;

/// <summary>
/// Manages the companion's in-session conversation history.
///
/// Messages are in-memory only for the current app session.
/// The companion does not persist conversations to disk — conversations
/// are ephemeral by design so the user retains privacy.
///
/// Threading:
///   Writes (AddUserMessage, AddSystemMessage, ClearSession) must be called
///   on the UI thread. MessageAdded fires on the calling thread.
///   Messages is safe to read on the UI thread without locking.
/// </summary>
public interface ICompanionSessionManager
{
    /// <summary>All messages in chronological order (oldest first).</summary>
    IReadOnlyList<CompanionMessage> Messages { get; }

    /// <summary>Raised when a new message is appended to Messages.</summary>
    event EventHandler<CompanionMessage>? MessageAdded;

    /// <summary>Appends a user-authored message to the session.</summary>
    void AddUserMessage(string text);

    /// <summary>
    /// Appends a system-authored message to the session.
    /// The category determines glyph and accent colour in the companion UI.
    /// </summary>
    void AddSystemMessage(
        string                   text,
        CompanionMessageCategory category = CompanionMessageCategory.Answer,
        string?                  actionId = null);

    /// <summary>Clears all messages from the current session.</summary>
    void ClearSession();

    /// <summary>Builds the welcome message shown when the companion first opens.</summary>
    CompanionMessage BuildWelcomeMessage();
}
