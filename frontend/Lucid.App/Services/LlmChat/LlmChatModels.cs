namespace Lucid.Services.LlmChat;

// ── Ollama REST API DTOs ───────────────────────────────────────────────────────
// These mirror the JSON shape of Ollama's /api/chat and /api/tags endpoints.
// Property names are lowercase to match Ollama's JSON without extra attributes.

public sealed class OllamaMessage
{
    public string role    { get; set; } = string.Empty;
    public string content { get; set; } = string.Empty;
}

public sealed class OllamaChatRequest
{
    public string             model    { get; set; } = string.Empty;
    public List<OllamaMessage> messages { get; set; } = [];
    public bool               stream   { get; set; } = true;
    public OllamaOptions      options  { get; set; } = new();
}

public sealed class OllamaOptions
{
    public int   num_ctx     { get; set; } = 4096;   // context window tokens
    public float temperature { get; set; } = 0.7f;
    public int   num_predict { get; set; } = 1024;   // max generated tokens
}

public sealed class OllamaChatChunk
{
    public OllamaMessage? message { get; set; }
    public bool           done    { get; set; }
    public string?        error   { get; set; }
}

public sealed class OllamaTagsResponse
{
    public List<OllamaModelInfo>? models { get; set; }
}

public sealed class OllamaModelInfo
{
    public string? name { get; set; }
}

// ── LLM chat service status ────────────────────────────────────────────────────

public enum LlmStatus
{
    /// <summary>Ollama is running and the model is ready.</summary>
    Ready,
    /// <summary>Ollama is not installed or not running.</summary>
    OllamaNotAvailable,
    /// <summary>Ollama is running but the required model has not been pulled yet.</summary>
    ModelNotPulled,
    /// <summary>Status has not yet been checked.</summary>
    Unknown,
}
