using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Services.Ai;

/// <summary>
/// Claude through the official Anthropic .NET SDK. The SDK is given the typed HttpClient from
/// IHttpClientFactory, so the same resilience pipeline (timeouts, retries, logging) applies as for Ollama.
/// SDK-level retries are switched off to avoid retrying twice.
/// </summary>
public sealed class ClaudeProvider : ILlmProvider
{
    private readonly AnthropicClient _client;
    private readonly ClaudeOptions _options;
    private readonly ILogger<ClaudeProvider> _logger;

    public ClaudeProvider(HttpClient http, IOptions<LlmOptions> options, ILogger<ClaudeProvider> logger)
    {
        _options = options.Value.Claude;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("LLM_PROVIDER=claude requires the ANTHROPIC_API_KEY environment variable.");
        }

        var timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
        _client = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? new AnthropicClient { ApiKey = _options.ApiKey, HttpClient = http, MaxRetries = 0, Timeout = timeout }
            : new AnthropicClient { ApiKey = _options.ApiKey, HttpClient = http, MaxRetries = 0, Timeout = timeout, BaseUrl = _options.BaseUrl };
    }

    public string Name => "claude";

    public string Model => _options.Model;

    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        Message message;
        try
        {
            message = await _client.Messages.Create(
                new MessageCreateParams
                {
                    Model = _options.Model,
                    MaxTokens = Math.Max(request.MaxTokens, _options.MaxTokens),
                    System = request.SystemPrompt,
                    Messages = [new UserMessageParam { Content = request.UserPrompt }],
                },
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Claude request failed");
            throw new LlmException($"Claude request failed: {ex.Message}", ex);
        }

        if (message.StopReason == StopReason.Refusal)
        {
            throw new LlmException("Claude declined to answer this request.");
        }

        var text = string.Join(
            "\n",
            message.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();
        if (text.Length == 0)
        {
            throw new LlmException("Claude returned no text content.");
        }

        return new LlmResult(
            text,
            message.Model.ToString() ?? _options.Model,
            (int?)message.Usage?.InputTokens,
            (int?)message.Usage?.OutputTokens);
    }
}
