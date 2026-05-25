namespace Lucid.Services.LlmChat;

/// <summary>
/// Ollama-backed LLM conversation service.
///
/// Each call to <see cref="StreamResponseAsync"/> builds a fresh system prompt
/// from live platform data, appends it as the system role message, then sends
/// the full conversation history to Ollama's /api/chat endpoint with streaming.
///
/// Conversation history is kept in memory (last <see cref="MaxTurns"/> turns)
/// so the LLM has context for follow-up questions.
///
/// Threading: StreamResponseAsync runs on the caller's thread for setup, then
/// the streaming loop runs on a thread-pool thread. onChunk is invoked on that
/// thread-pool thread — callers must dispatch to UI thread if needed.
/// </summary>
public sealed class LlmChatService : ILlmChatService
{
    private const int MaxTurns = 20;  // 10 user + 10 assistant pairs

    private readonly OllamaClient             _client = new();
    private readonly List<OllamaMessage>      _history = [];
    private readonly SemaphoreSlim            _lock    = new(1, 1);

    public LlmStatus Status  { get; private set; } = LlmStatus.Unknown;
    public bool      IsReady => Status == LlmStatus.Ready;

    // ── Status check ───────────────────────────────────────────────────────────

    public async Task<LlmStatus> CheckStatusAsync(CancellationToken ct = default)
    {
        var available = await _client.IsAvailableAsync(ct).ConfigureAwait(false);
        if (!available)
        {
            Status = LlmStatus.OllamaNotAvailable;
            return Status;
        }

        var modelReady = await _client.IsModelReadyAsync(ct).ConfigureAwait(false);
        Status = modelReady ? LlmStatus.Ready : LlmStatus.ModelNotPulled;
        return Status;
    }

    // ── Streaming response ─────────────────────────────────────────────────────

    public async Task StreamResponseAsync(
        string         userMessage,
        Action<string> onChunk,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Build the full message list: system context + history + new user message
            var systemPrompt = LlmSystemContextBuilder.Build();

            var messages = new List<OllamaMessage>
            {
                new() { role = "system", content = systemPrompt }
            };

            // Add conversation history (trimmed to MaxTurns)
            var historySlice = _history.Count > MaxTurns
                ? _history.Skip(_history.Count - MaxTurns).ToList()
                : _history;
            messages.AddRange(historySlice);

            // Add the new user message
            messages.Add(new OllamaMessage { role = "user", content = userMessage });

            // Record user message in history
            _history.Add(new OllamaMessage { role = "user", content = userMessage });

            // Stream the response
            var responseBuilder = new System.Text.StringBuilder();
            await foreach (var chunk in _client.ChatStreamAsync(messages, ct).ConfigureAwait(false))
            {
                responseBuilder.Append(chunk);
                onChunk(chunk);
            }

            // Record assistant response in history
            var fullResponse = responseBuilder.ToString();
            if (!string.IsNullOrEmpty(fullResponse))
                _history.Add(new OllamaMessage { role = "assistant", content = fullResponse });

            // Trim history if too long
            while (_history.Count > MaxTurns)
                _history.RemoveAt(0);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── History ────────────────────────────────────────────────────────────────

    public void ClearHistory() => _history.Clear();
}
