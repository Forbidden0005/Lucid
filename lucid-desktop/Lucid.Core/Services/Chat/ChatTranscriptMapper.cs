using Lucid.Services.Companion;
using Lucid.Services.LlmChat;

namespace Lucid.Services.Chat;

/// <summary>
/// Translates between the three representations a conversation takes:
///
///   <see cref="CompanionMessage"/>    — what the UI renders
///   <see cref="ChatTranscriptEntry"/> — what a session stores
///   <see cref="LlmTurn"/>             — what the model is given as context
///
/// Kept as a pure mapper in Core so the rules — particularly which messages the
/// model is allowed to see when a session is resumed — are covered by tests
/// rather than buried in a ViewModel.
/// </summary>
public static class ChatTranscriptMapper
{
    /// <summary>Projects a rendered message down to its durable fields.</summary>
    public static ChatTranscriptEntry ToEntry(CompanionMessage message) => new()
    {
        Id        = message.Id,
        Role      = message.Role,
        Text      = message.Text,
        Timestamp = message.Timestamp,
        Category  = message.Category,
    };

    /// <summary>
    /// Rebuilds a renderable message from a stored entry. Enrichment fields
    /// (action chips, evidence badges, confidence) are intentionally left empty:
    /// they describe live system state at the time of the answer and would be
    /// misleading if replayed hours later against a machine that has changed.
    /// </summary>
    public static CompanionMessage ToMessage(ChatTranscriptEntry entry) => new()
    {
        Id        = entry.Id,
        Role      = entry.Role,
        Text      = entry.Text,
        Timestamp = entry.Timestamp,
        Category  = entry.Category,
    };

    public static IReadOnlyList<CompanionMessage> ToMessages(IEnumerable<ChatTranscriptEntry> entries)
        => entries.Select(ToMessage).ToList();

    /// <summary>
    /// Builds the model-facing history for a resumed session.
    ///
    /// Only genuine conversation turns are included. Setup warnings, transport
    /// errors and the welcome copy are Lucid talking *about* itself, not answers
    /// it gave — feeding them back as assistant turns teaches the model to
    /// imitate error messages. Empty messages (an answer that was cancelled
    /// before any text arrived) are dropped for the same reason.
    /// </summary>
    public static IReadOnlyList<LlmTurn> ToLlmTurns(IEnumerable<ChatTranscriptEntry> entries)
    {
        var turns = new List<LlmTurn>();

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Text)) continue;

            if (entry.Role == CompanionMessageRole.User)
            {
                turns.Add(new LlmTurn(LlmTurnRole.User, entry.Text));
                continue;
            }

            if (IsModelAuthored(entry.Category))
                turns.Add(new LlmTurn(LlmTurnRole.Assistant, entry.Text));
        }

        return turns;
    }

    /// <summary>
    /// True when a system-role message actually came from the model, as opposed
    /// to being app-generated chrome.
    /// </summary>
    private static bool IsModelAuthored(CompanionMessageCategory category) => category
        is CompanionMessageCategory.Answer
        or CompanionMessageCategory.Insight;
}
