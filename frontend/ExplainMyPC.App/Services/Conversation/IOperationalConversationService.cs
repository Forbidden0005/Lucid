namespace ExplainMyPC.Services.Conversation;

/// <summary>
/// Richer companion conversation interface for Phase 17C.
///
/// Processes a user's operational query into a structured response
/// grounded entirely in live platform service data. Deterministic
/// keyword matching — not an LLM. Nothing leaves the device.
///
/// Also surfaces lightweight contextual suggestions derived from
/// the desktop context layer (what folder the user is in, clipboard
/// contents, etc.) so the companion can make proactive suggestions
/// without interrupting the user.
/// </summary>
public interface IOperationalConversationService
{
    /// <summary>
    /// Processes a query and returns a rich operational response.
    /// Never throws — returns a low-confidence explanation on failure.
    /// </summary>
    Task<OperationalResponse> ProcessQueryAsync(
        OperationalPrompt prompt,
        CancellationToken ct = default);

    /// <summary>
    /// Returns lightweight contextual suggestions derived from the current
    /// desktop context snapshot. Empty list when no relevant context is available.
    /// Synchronous — reads the last cached snapshot; never triggers a new scan.
    /// </summary>
    IReadOnlyList<ContextualSuggestion> GetContextualSuggestions();
}
