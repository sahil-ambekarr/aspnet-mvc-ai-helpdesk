using System.Net;
using System.Text.Json;
using Helpdesk.Web.Models;
using Helpdesk.Web.Services.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Helpdesk.Tests;

public class MockProviderTests
{
    private readonly MockProvider _mock = new();

    [Fact]
    public async Task Same_input_gives_same_output()
    {
        var request = new LlmRequest(AiInteractionType.TicketSummary, "sys", "Title: Printer offline\nStatus: Open\n--- Comment 1 by A");
        var a = await _mock.CompleteAsync(request);
        var b = await _mock.CompleteAsync(request);

        Assert.Equal(a, b);
        Assert.Contains("Printer offline", a.Text);
        Assert.Contains("1 comment(s)", a.Text);
    }

    [Fact]
    public async Task Kb_answer_without_sources_returns_not_found_token()
    {
        var result = await _mock.CompleteAsync(new LlmRequest(AiInteractionType.KbAnswer, "sys", "<question>x</question>"));
        Assert.Equal(PromptBuilder.NotFoundToken, result.Text);
    }

    [Fact]
    public async Task Reply_draft_uses_requester_first_name_and_agent()
    {
        var prompt = "Title: VPN drops\nRequester: Dana Whitfield\nReplying agent: Priya Nair";
        var result = await _mock.CompleteAsync(new LlmRequest(AiInteractionType.ReplyDraft, "sys", prompt));

        Assert.StartsWith("[mock] Hi Dana,", result.Text);
        Assert.Contains("Priya Nair", result.Text);
    }
}

public class OllamaProviderTests
{
    private const string OkResponse = """
        {"model":"llama3.2","message":{"role":"assistant","content":"  Restart the VPN client.  "},"done":true,"prompt_eval_count":120,"eval_count":14}
        """;

    private static (OllamaProvider Provider, FakeHttpMessageHandler Handler) Create()
    {
        var handler = new FakeHttpMessageHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://ollama.test/") };
        var options = Options.Create(new LlmOptions { Ollama = new OllamaOptions { Model = "llama3.2", NumCtx = 2048 } });
        return (new OllamaProvider(http, options, NullLogger<OllamaProvider>.Instance), handler);
    }

    [Fact]
    public async Task Sends_non_streaming_chat_request_and_parses_text_and_token_counts()
    {
        var (provider, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, OkResponse);

        var result = await provider.CompleteAsync(new LlmRequest(AiInteractionType.KbAnswer, "system text", "user text", MaxTokens: 123));

        Assert.Equal("Restart the VPN client.", result.Text);
        Assert.Equal(120, result.InputTokens);
        Assert.Equal(14, result.OutputTokens);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://ollama.test/api/chat", request.RequestUri!.ToString());

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("llama3.2", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("user text", root.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.Equal(123, root.GetProperty("options").GetProperty("num_predict").GetInt32());
        Assert.Equal(2048, root.GetProperty("options").GetProperty("num_ctx").GetInt32());
    }

    [Fact]
    public async Task Http_error_becomes_LlmException()
    {
        var (provider, handler) = Create();
        handler.Enqueue(HttpStatusCode.NotFound, """{"error":"model 'llama3.2' not found"}""");

        var ex = await Assert.ThrowsAsync<LlmException>(() => provider.CompleteAsync(new LlmRequest(AiInteractionType.KbAnswer, "s", "u")));
        Assert.Contains("404", ex.Message);
    }

    [Fact]
    public async Task Empty_content_becomes_LlmException()
    {
        var (provider, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"model":"llama3.2","message":{"role":"assistant","content":"   "}}""");

        await Assert.ThrowsAsync<LlmException>(() => provider.CompleteAsync(new LlmRequest(AiInteractionType.KbAnswer, "s", "u")));
    }

    [Fact]
    public async Task Invalid_json_becomes_LlmException()
    {
        var (provider, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, "<html>proxy error</html>", "text/html");

        await Assert.ThrowsAsync<LlmException>(() => provider.CompleteAsync(new LlmRequest(AiInteractionType.KbAnswer, "s", "u")));
    }

    [Fact]
    public async Task Connection_failure_becomes_LlmException()
    {
        var (provider, handler) = Create();
        handler.EnqueueException(new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsAsync<LlmException>(() => provider.CompleteAsync(new LlmRequest(AiInteractionType.KbAnswer, "s", "u")));
        Assert.Contains("connection refused", ex.Message);
    }
}

public class ClaudeProviderTests
{
    private const string OkResponse = """
        {
          "id": "msg_test",
          "type": "message",
          "role": "assistant",
          "model": "claude-haiku-5-5",
          "content": [ { "type": "text", "text": "- Issue: VPN drops hourly" } ],
          "stop_reason": "end_turn",
          "stop_sequence": null,
          "usage": { "input_tokens": 321, "output_tokens": 45 }
        }
        """;

    private static (ClaudeProvider Provider, FakeHttpMessageHandler Handler) Create(string? apiKey = "test-key-not-real")
    {
        var handler = new FakeHttpMessageHandler();
        var http = new HttpClient(handler);
        var options = Options.Create(new LlmOptions
        {
            Claude = new ClaudeOptions { ApiKey = apiKey, Model = "claude-haiku-5-5", BaseUrl = "https://claude.test", MaxTokens = 1024 },
        });
        return (new ClaudeProvider(http, options, NullLogger<ClaudeProvider>.Instance), handler);
    }

    [Fact]
    public void Requires_an_api_key()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Create(apiKey: null));
        Assert.Contains("ANTHROPIC_API_KEY", ex.Message);
    }

    [Fact]
    public async Task Calls_messages_api_through_the_sdk_and_parses_text_and_usage()
    {
        var (provider, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, OkResponse);

        var result = await provider.CompleteAsync(new LlmRequest(AiInteractionType.TicketSummary, "system text", "ticket text"));

        Assert.Equal("- Issue: VPN drops hourly", result.Text);
        Assert.Equal("claude-haiku-5-5", result.Model);
        Assert.Equal(321, result.InputTokens);
        Assert.Equal(45, result.OutputTokens);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v1/messages", request.RequestUri!.AbsolutePath);
        // The SDK decides the auth header format; SDK 13.9 sends the key as a bearer token.
        var auth = request.Headers.TryGetValues("x-api-key", out var keys) ? keys.Single() : request.Headers.Authorization?.Parameter;
        Assert.Equal("test-key-not-real", auth);
        Assert.True(request.Headers.Contains("anthropic-version"));

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("claude-haiku-5-5", root.GetProperty("model").GetString());
        Assert.Equal(1024, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal("system text", root.GetProperty("system").GetString());
        Assert.Equal("user", root.GetProperty("messages")[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task Api_error_becomes_LlmException()
    {
        var (provider, handler) = Create();
        handler.Enqueue(HttpStatusCode.BadRequest, """{"type":"error","error":{"type":"invalid_request_error","message":"bad request"}}""");

        await Assert.ThrowsAsync<LlmException>(() => provider.CompleteAsync(new LlmRequest(AiInteractionType.TicketSummary, "s", "u")));
    }

    [Fact]
    public async Task Refusal_stop_reason_becomes_LlmException()
    {
        var (provider, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, OkResponse.Replace("\"end_turn\"", "\"refusal\""));

        await Assert.ThrowsAsync<LlmException>(() => provider.CompleteAsync(new LlmRequest(AiInteractionType.TicketSummary, "s", "u")));
    }
}

public class ProviderRegistrationTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings, HttpMessageHandler? primary = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddLlmProviders(config);
        if (primary is not null)
        {
            services.AddHttpClient<OllamaProvider>().ConfigurePrimaryHttpMessageHandler(() => primary);
        }
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("mock", typeof(MockProvider))]
    [InlineData("ollama", typeof(OllamaProvider))]
    [InlineData("OLLAMA", typeof(OllamaProvider))]
    public void Provider_is_selected_from_configuration(string name, Type expected)
    {
        using var sp = Build(new() { ["Llm:Provider"] = name });
        Assert.IsType(expected, sp.GetRequiredService<ILlmProvider>());
    }

    [Fact]
    public void LLM_PROVIDER_variable_overrides_config_section()
    {
        using var sp = Build(new() { ["Llm:Provider"] = "ollama", ["LLM_PROVIDER"] = "mock" });
        Assert.IsType<MockProvider>(sp.GetRequiredService<ILlmProvider>());
    }

    [Fact]
    public void Claude_is_only_enabled_with_an_api_key()
    {
        using var without = Build(new() { ["LLM_PROVIDER"] = "claude" });
        Assert.Throws<InvalidOperationException>(() => without.GetRequiredService<ILlmProvider>());

        using var with = Build(new() { ["LLM_PROVIDER"] = "claude", ["ANTHROPIC_API_KEY"] = "test-key-not-real", ["CLAUDE_MODEL"] = "claude-haiku-5-5" });
        var provider = Assert.IsType<ClaudeProvider>(with.GetRequiredService<ILlmProvider>());
        Assert.Equal("claude-haiku-5-5", provider.Model);
    }

    [Fact]
    public void Unknown_provider_is_rejected()
    {
        using var sp = Build(new() { ["LLM_PROVIDER"] = "gpt" });
        var ex = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<ILlmProvider>());
        Assert.Contains("ollama, claude or mock", ex.Message);
    }

    [Fact]
    public async Task Resilience_pipeline_retries_transient_failures()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.ServiceUnavailable, "busy")
            .Enqueue(HttpStatusCode.OK, """{"model":"llama3.2","message":{"role":"assistant","content":"ok"}}""");
        using var sp = Build(
            new() { ["LLM_PROVIDER"] = "ollama", ["Llm:RetryBaseDelayMs"] = "10", ["OLLAMA_BASE_URL"] = "http://ollama.test" },
            handler);

        var result = await sp.GetRequiredService<ILlmProvider>().CompleteAsync(new LlmRequest(AiInteractionType.KbAnswer, "s", "u"));

        Assert.Equal("ok", result.Text);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("http://ollama.test/api/chat", r.Request.RequestUri!.ToString()));
    }

    [Fact]
    public async Task Gives_up_after_max_retries()
    {
        var handler = new FakeHttpMessageHandler();
        for (var i = 0; i < 3; i++)
        {
            handler.Enqueue(HttpStatusCode.InternalServerError, "boom");
        }
        using var sp = Build(
            new() { ["LLM_PROVIDER"] = "ollama", ["Llm:RetryBaseDelayMs"] = "10", ["Llm:MaxRetryAttempts"] = "2" },
            handler);

        await Assert.ThrowsAsync<LlmException>(() =>
            sp.GetRequiredService<ILlmProvider>().CompleteAsync(new LlmRequest(AiInteractionType.KbAnswer, "s", "u")));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Attempt_timeout_is_enforced()
    {
        var slow = new SlowHandler(TimeSpan.FromSeconds(10));
        using var sp = Build(
            new()
            {
                ["LLM_PROVIDER"] = "ollama",
                ["Llm:AttemptTimeoutSeconds"] = "1",
                ["Llm:TimeoutSeconds"] = "2",
                ["Llm:MaxRetryAttempts"] = "0",
            },
            slow);

        var started = DateTime.UtcNow;
        await Assert.ThrowsAsync<LlmException>(() =>
            sp.GetRequiredService<ILlmProvider>().CompleteAsync(new LlmRequest(AiInteractionType.KbAnswer, "s", "u")));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    private sealed class SlowHandler(TimeSpan delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
