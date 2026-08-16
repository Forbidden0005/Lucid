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
public sealed class LlmChatService : ILlmChatService, IDisposable
{
    private const int MaxTurns = 20;  // 10 user + 10 assistant pairs

    private OllamaClient                  _client;   // mutable — swapped by ReconfigureAsync
    private readonly List<OllamaMessage>  _history = [];
    private readonly SemaphoreSlim        _lock    = new(1, 1);
    private readonly Func<string>         _systemPromptFactory;
    private bool                          _disposed;

    /// <summary>
    /// Constructs the chat service. Endpoint and model are passed from
    /// the composition root so the service never reads back into the
    /// static locator — avoiding circular dependencies.
    ///
    /// <paramref name="systemPromptFactory"/> supplies the live system-context
    /// prompt, rebuilt on every message. Composing that prompt means reading a
    /// dozen-odd services at once, which is an application-layer concern — so
    /// the factory is injected rather than called statically from here. The App
    /// passes <c>LlmSystemContextBuilder.Build</c>; tests can pass a stub.
    /// Omitting it yields an empty system prompt.
    /// </summary>
    public LlmChatService(
        string        baseUrl             = OllamaClient.DefaultBaseUrl,
        string        modelName           = OllamaClient.DefaultModelName,
        Func<string>? systemPromptFactory = null)
    {
        _client              = new OllamaClient(baseUrl, modelName);
        _systemPromptFactory = systemPromptFactory ?? (static () => string.Empty);
    }

    public string    ModelName => _client.ModelName;
    public LlmStatus Status    { get; private set; } = LlmStatus.Unknown;
    public bool      IsReady   => Status == LlmStatus.Ready;

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
            var systemPrompt = _systemPromptFactory();

            var messages = new List<OllamaMessage>
            {
                new() { role = "system", content = systemPrompt }
            };

            // Add conversation history (trimmed to MaxTurns)
            var historySlice = _history.Count > MaxTurns
                ? _history.Skip(_history.Count - MaxTurns).ToList()
                : _history;
            messages.AddRange(historySlice);

            // Add the new user message to the request (but NOT to history yet —
            // we defer the write until we have a response so cancellation or
            // network errors don't leave an orphaned user message with no reply).
            messages.Add(new OllamaMessage { role = "user", content = userMessage });

            // Stream the response
            var responseBuilder = new System.Text.StringBuilder();
            try
            {
                await foreach (var chunk in _client.ChatStreamAsync(messages, ct).ConfigureAwait(false))
                {
                    responseBuilder.Append(chunk);
                    onChunk(chunk);
                }
            }
            catch (OperationCanceledException)
            {
                // User cancelled mid-stream. If any text arrived, persist both the
                // user turn and the partial assistant turn so follow-up messages have
                // coherent context. Re-throw so the caller can handle the cancellation.
                CommitTurn(userMessage, responseBuilder.ToString());
                throw;
            }

            // Full response received — commit both turns to history.
            CommitTurn(userMessage, responseBuilder.ToString());
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Appends a user + assistant message pair to history and trims to MaxTurns.
    /// Only records the assistant side when the response is non-empty.
    /// </summary>
    private void CommitTurn(string userMessage, string assistantResponse)
    {
        _history.Add(new OllamaMessage { role = "user", content = userMessage });

        if (!string.IsNullOrEmpty(assistantResponse))
            _history.Add(new OllamaMessage { role = "assistant", content = assistantResponse });

        // Use RemoveRange for O(1) front-removal instead of O(n) per-element loop
        if (_history.Count > MaxTurns)
            _history.RemoveRange(0, _history.Count - MaxTurns);
    }

    // ── History ────────────────────────────────────────────────────────────────

    public void ClearHistory() => _history.Clear();

    /// <summary>
    /// Rehydrates history from a saved session. Applies the same MaxTurns cap as
    /// a live conversation, keeping the most recent turns — a long resumed
    /// session must not silently blow past the model's context window.
    /// </summary>
    public void RestoreHistory(IReadOnlyList<LlmTurn> turns)
    {
        _history.Clear();

        var slice = turns.Count > MaxTurns ? turns.Skip(turns.Count - MaxTurns) : turns;

        foreach (var turn in slice)
        {
            _history.Add(new OllamaMessage
            {
                role    = turn.Role == LlmTurnRole.User ? "user" : "assistant",
                content = turn.Text,
            });
        }
    }

    // ── Reconfiguration ────────────────────────────────────────────────────────

    /// <summary>
    /// Swaps the underlying Ollama client to point at a new endpoint and/or model.
    ///
    /// Acquires the stream lock so any in-flight response finishes cleanly before
    /// the swap occurs — the user never sees a torn stream. After the swap:
    ///   • <see cref="Status"/> is reset to Unknown so the next chat open triggers
    ///     a fresh availability check with the new endpoint.
    ///   • Conversation history is cleared — history built against one model is
    ///     not coherent context for a different model or server.
    ///   • The old client is disposed so its HttpClient instances are returned
    ///     to the connection pool.
    /// </summary>
    public async Task ReconfigureAsync(string baseUrl, string modelName)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var old = _client;
            _client = new OllamaClient(baseUrl, modelName);
            Status  = LlmStatus.Unknown;
            _history.Clear();
            old.Dispose();
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Dispose ────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client.Dispose();
        _lock.Dispose();
    }
}
