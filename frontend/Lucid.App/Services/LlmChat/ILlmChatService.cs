namespace Lucid.Services.LlmChat;

/// <summary>
/// LLM-powered conversational interface for Lucid.
/// Backed by Ollama running locally — no cloud, no API keys, free forever.
/// </summary>
public interface ILlmChatService
{
    /// <summary>The Ollama model name this service is configured to use.</summary>
    string ModelName { get; }

    /// <summary>Current availability status of Ollama and the required model.</summary>
    LlmStatus Status { get; }

    /// <summary>True when Ollama is running and the configured model is ready.</summary>
    bool IsReady { get; }

    /// <summary>
    /// Checks Ollama availability and updates <see cref="Status"/>.
    /// Call once at startup and again when the user opens the chat.
    /// </summary>
    Task<LlmStatus> CheckStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Streams a response to <paramref name="userMessage"/>, calling
    /// <paramref name="onChunk"/> for each text fragment as it is generated.
    /// The system context is rebuilt fresh from live platform data on each call.
    ///
    /// Threading: <paramref name="onChunk"/> is invoked on a thread-pool thread.
    /// The caller must marshal to the UI thread if needed.
    /// </summary>
    Task StreamResponseAsync(
        string         userMessage,
        Action<string> onChunk,
        CancellationToken ct = default);

    /// <summary>Clears the in-memory conversation history.</summary>
    void ClearHistory();
}
