using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lucid.Services.LlmChat;

/// <summary>
/// Thin HTTP client for the Ollama local inference server (localhost:11434).
///
/// Two HttpClient instances are used intentionally:
///   _ping   — 5s timeout for availability checks that must fail fast.
///   _stream — InfiniteTimeSpan for chat streaming that can take any duration.
/// </summary>
public sealed class OllamaClient : IDisposable
{
    private const string BaseUrl   = "http://localhost:11434";
    public  const string ModelName = "llama3.2:3b";

    private static readonly JsonSerializerOptions _json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _ping   = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly HttpClient _stream = new() { Timeout = Timeout.InfiniteTimeSpan };

    // ── Availability ───────────────────────────────────────────────────────────

    /// <summary>Returns true if Ollama is running and reachable at localhost:11434.</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await _ping.GetAsync($"{BaseUrl}/api/tags", ct).ConfigureAwait(false);
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>
    /// Returns true if the llama3.2:3b model (or any 3b variant) is already pulled.
    /// </summary>
    public async Task<bool> IsModelReadyAsync(CancellationToken ct = default)
    {
        try
        {
            var tags = await _ping
                .GetFromJsonAsync<OllamaTagsResponse>($"{BaseUrl}/api/tags", ct)
                .ConfigureAwait(false);

            return tags?.models?.Any(m =>
                m.name is not null &&
                m.name.Contains("llama3.2", StringComparison.OrdinalIgnoreCase)) == true;
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

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/chat")
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
