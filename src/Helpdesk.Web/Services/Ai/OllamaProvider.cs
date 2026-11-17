using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Services.Ai;

/// <summary>Calls Ollama's /api/chat endpoint (non-streaming). Typed HttpClient; retries/timeouts come from the resilience pipeline.</summary>
public sealed class OllamaProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaProvider> _logger;

    public OllamaProvider(HttpClient http, IOptions<LlmOptions> options, ILogger<OllamaProvider> logger)
    {
        _http = http;
        _options = options.Value.Ollama;
        _logger = logger;
    }

    public string Name => "ollama";

    public string Model => _options.Model;

    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var body = new ChatRequest(
            _options.Model,
            new[]
            {
                new ChatMessage("system", request.SystemPrompt),
                new ChatMessage("user", request.UserPrompt),
            },
            Stream: false,
            new ChatOptions(_options.Temperature, request.MaxTokens, _options.NumCtx));

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync("api/chat", body, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Ollama request failed");
            throw new LlmException($"Ollama request failed: {ex.Message}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Ollama returned {Status}: {Body}", (int)response.StatusCode, Truncate(error, 300));
                throw new LlmException($"Ollama returned HTTP {(int)response.StatusCode}.");
            }

            ChatResponse? parsed;
            try
            {
                parsed = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct);
            }
            catch (JsonException ex)
            {
                throw new LlmException("Ollama returned invalid JSON.", ex);
            }

            var text = parsed?.Message?.Content?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                throw new LlmException("Ollama returned an empty response.");
            }

            return new LlmResult(text, parsed!.Model ?? _options.Model, parsed.PromptEvalCount, parsed.EvalCount);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] ChatMessage[] Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("options")] ChatOptions Options);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatOptions(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("num_predict")] int NumPredict,
        [property: JsonPropertyName("num_ctx")] int NumCtx);

    private sealed class ChatResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }

        [JsonPropertyName("prompt_eval_count")]
        public int? PromptEvalCount { get; set; }

        [JsonPropertyName("eval_count")]
        public int? EvalCount { get; set; }
    }
}
