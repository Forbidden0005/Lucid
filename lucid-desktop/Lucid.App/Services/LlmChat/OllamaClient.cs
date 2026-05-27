using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lucid.Services.LlmChat;

/// <summary>
/// Thin HTTP client for the Ollama local inference server.
///
/// Endpoint and model are injected at construction time so they can be
/// read from <see cref="AppSettings"/> without hardcoding.  The defaults
/// match the standard Ollama install so the app works out-of-the-box.
///
/// Two HttpClient instances are used intentionally:
///   _ping   — 5s timeout for availability checks that must fail fast.
///   _stream — InfiniteTimeSpan for chat streaming that can take any duration.
/// </summary>
public sealed class OllamaClient : IDisposable
{
    /// <summary>Default Ollama base URL (standard installation).</summary>
    public const string DefaultBaseUrl   = "http://localhost:11434";
    /// <summary>Default model name — small, fast, good quality for local use.</summary>
    public const string DefaultModelName = "llama3.2:3b";

    private readonly string _baseUrl;
    private readonly string _modelName;

    private static readonly JsonSerializerOptions _json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _ping   = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly HttpClient _stream = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>
    /// Constructs an OllamaClient.
    /// </summary>
    /// <param name="baseUrl">Ollama server URL — defaults to <c>http://localhost:11434</c>.</param>
    /// <param name="modelName">Ollama model — defaults to <c>llama3.2:3b</c>.</param>
    public OllamaClient(
        string baseUrl   = DefaultBaseUrl,
        string modelName = DefaultModelName)
    {
        _baseUrl   = string.IsNullOrWhiteSpace(baseUrl)   ? DefaultBaseUrl   : baseUrl.TrimEnd('/');
        _modelName = string.IsNullOrWhiteSpace(modelName) ? DefaultModelName : modelName.Trim();
    }

    /// <summary>The model name this client is configured to use.</summary>
    public string ModelName => _modelName;

    // ── Availability ───────────────────────────────────────────────────────────

    /// <summary>Returns true if Ollama is running and reachable at localhost:11434.</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await _ping.GetAsync($"{_baseUrl}/api/tags", ct).ConfigureAwait(false);
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>
    /// Returns true if the configured model is already pulled in Ollama.
    /// Uses an exact, case-insensitive match against the model name supplied
    /// at construction time (see <see cref="ModelName"/>).
    /// </summary>
    public async Task<bool> IsModelReadyAsync(CancellationToken ct = default)
    {
        try
        {
            var tags = await _ping
                .GetFromJsonAsync<OllamaTagsResponse>($"{_baseUrl}/api/tags", ct)
                .ConfigureAwait(false);

            return tags?.models?.Any(m =>
                m.name is not null &&
                m.name.Equals(_modelName, StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch { return false; }
    }

    // ── Streaming chat ─────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a conversation to Ollama and yields text chunks as they are generated.
    /// The caller is responsible for providing the full message history including
    /// the system prompt as the first message.
    ///
    /// Throws <see cref="HttpRequestException"/> if Ollama is unreachable.
    /// Throws <see cref="InvalidOperationException"/> if Ollama returns an error.
    /// </summary>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        IReadOnlyList<OllamaMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new OllamaChatRequest
        {
            model    = ModelName,
            messages = [.. messages],
            stream   = true,
            options  = new OllamaOptions
            {
                num_ctx     = 4096,
                temperature = 0.7f,
                num_predict = 1024,
            },
        };

        var body = JsonSerializer.Serialize(request, _json);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
            { Content = content };

        using var response = await _stream
            .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content
            .ReadAsStreamAsync(ct).ConfigureAwait(false);

        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            OllamaChatChunk? chunk;
            try   { chunk = JsonSerializer.Deserialize<OllamaChatChunk>(line, _json); }
            catch { continue; }

            if (chunk is null) continue;

            if (chunk.error is not null)
                throw new InvalidOperationException($"Ollama: {chunk.error}");

            var text = chunk.message?.content;
            if (!string.IsNullOrEmpty(text))
                yield return text;

            if (chunk.done) break;
        }
    }

    public void Dispose()
    {
        _ping.Dispose();
        _stream.Dispose();
    }
}
