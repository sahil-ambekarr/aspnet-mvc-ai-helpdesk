using Helpdesk.Web.Models;

namespace Helpdesk.Web.Services.Ai;

/// <summary>A single-turn completion: one system prompt, one user prompt, text back.</summary>
public interface ILlmProvider
{
    /// <summary>Short provider id stored in the audit table ("ollama", "claude", "mock").</summary>
    string Name { get; }

    string Model { get; }

    Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct = default);
}

public sealed record LlmRequest(AiInteractionType Feature, string SystemPrompt, string UserPrompt, int MaxTokens = 400);

public sealed record LlmResult(string Text, string Model, int? InputTokens = null, int? OutputTokens = null);

/// <summary>Raised by providers for any upstream failure (HTTP error, timeout, empty or refused response).</summary>
public sealed class LlmException : Exception
{
    public LlmException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>"ollama" (default), "claude" or "mock". Overridden by the LLM_PROVIDER environment variable.</summary>
    public string Provider { get; set; } = "ollama";

    /// <summary>Upper bound for one call including retries. CPU-only Ollama can be slow, hence the generous default.</summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>Timeout for a single HTTP attempt.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 240;

    public int MaxRetryAttempts { get; set; } = 2;

    public int RetryBaseDelayMs { get; set; } = 1000;

    public OllamaOptions Ollama { get; set; } = new();

    public ClaudeOptions Claude { get; set; } = new();
}

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3.2";
    public double Temperature { get; set; } = 0.2;

    /// <summary>Context window passed to Ollama. Ticket threads plus KB excerpts fit in 4096 tokens.</summary>
    public int NumCtx { get; set; } = 4096;
}

public sealed class ClaudeOptions
{
    /// <summary>Read from ANTHROPIC_API_KEY. The Claude provider refuses to start without it.</summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-haiku-5-5";

    /// <summary>Optional override, used by tests to point the SDK at a fake handler.</summary>
    public string? BaseUrl { get; set; }

    public int MaxTokens { get; set; } = 1024;
}
