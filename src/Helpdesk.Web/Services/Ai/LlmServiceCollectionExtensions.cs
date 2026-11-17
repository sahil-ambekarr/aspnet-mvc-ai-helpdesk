using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Helpdesk.Web.Services.Ai;

public static class LlmServiceCollectionExtensions
{
    /// <summary>
    /// Registers the configured <see cref="ILlmProvider"/>. Flat environment variables (LLM_PROVIDER, OLLAMA_BASE_URL,
    /// OLLAMA_MODEL, CLAUDE_MODEL, ANTHROPIC_API_KEY, LLM_TIMEOUT_SECONDS) override the "Llm" config section so the
    /// same variable names work across all my AI repos.
    /// </summary>
    public static IServiceCollection AddLlmProviders(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<LlmOptions>()
            .Bind(config.GetSection(LlmOptions.SectionName))
            .PostConfigure(o => ApplyEnvironmentOverrides(o, config))
            .Validate(o => o.TimeoutSeconds > 0 && o.AttemptTimeoutSeconds > 0 && o.MaxRetryAttempts >= 0, "Invalid LLM timeout/retry settings.")
            .ValidateOnStart();

        services.AddHttpClient<OllamaProvider>((sp, http) =>
            {
                var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
                http.BaseAddress = new Uri(options.Ollama.BaseUrl.TrimEnd('/') + "/");
                http.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns timeouts
            })
            .AddResilienceHandler("ollama", ConfigurePipeline);

        services.AddHttpClient<ClaudeProvider>(http => http.Timeout = Timeout.InfiniteTimeSpan)
            .AddResilienceHandler("claude", ConfigurePipeline);

        services.AddSingleton<MockProvider>();

        services.AddTransient<ILlmProvider>(sp =>
        {
            var provider = sp.GetRequiredService<IOptions<LlmOptions>>().Value.Provider.Trim().ToLowerInvariant();
            return provider switch
            {
                "ollama" => sp.GetRequiredService<OllamaProvider>(),
                "claude" => sp.GetRequiredService<ClaudeProvider>(),
                "mock" => sp.GetRequiredService<MockProvider>(),
                _ => throw new InvalidOperationException($"Unknown LLM provider '{provider}'. Use ollama, claude or mock."),
            };
        });

        return services;
    }

    internal static void ApplyEnvironmentOverrides(LlmOptions o, IConfiguration config)
    {
        if (config["LLM_PROVIDER"] is { Length: > 0 } provider)
        {
            o.Provider = provider;
        }
        if (config["OLLAMA_BASE_URL"] is { Length: > 0 } baseUrl)
        {
            o.Ollama.BaseUrl = baseUrl;
        }
        if (config["OLLAMA_MODEL"] is { Length: > 0 } ollamaModel)
        {
            o.Ollama.Model = ollamaModel;
        }
        if (config["CLAUDE_MODEL"] is { Length: > 0 } claudeModel)
        {
            o.Claude.Model = claudeModel;
        }
        if (config["ANTHROPIC_API_KEY"] is { Length: > 0 } apiKey)
        {
            o.Claude.ApiKey = apiKey;
        }
        if (int.TryParse(config["LLM_TIMEOUT_SECONDS"], out var timeout) && timeout > 0)
        {
            o.TimeoutSeconds = timeout;
            o.AttemptTimeoutSeconds = Math.Min(o.AttemptTimeoutSeconds, timeout);
        }
    }

    private static void ConfigurePipeline(ResiliencePipelineBuilder<HttpResponseMessage> builder, ResilienceHandlerContext context)
    {
        var options = context.ServiceProvider.GetRequiredService<IOptions<LlmOptions>>().Value;
        var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Helpdesk.Llm.Resilience");

        // Outer timeout covers all attempts; inner timeout bounds a single attempt.
        builder.AddTimeout(TimeSpan.FromSeconds(options.TimeoutSeconds));
        if (options.MaxRetryAttempts > 0)
        {
            AddRetry(builder, options, logger);
        }
        builder.AddTimeout(TimeSpan.FromSeconds(options.AttemptTimeoutSeconds));
    }

    private static void AddRetry(ResiliencePipelineBuilder<HttpResponseMessage> builder, LlmOptions options, ILogger logger)
    {
        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = options.MaxRetryAttempts,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(options.RetryBaseDelayMs),
            ShouldRetryAfterHeader = true,
            OnRetry = args =>
            {
                logger.LogWarning(
                    "LLM call attempt {Attempt} failed ({Reason}); retrying in {Delay}",
                    args.AttemptNumber + 1,
                    args.Outcome.Exception?.GetType().Name ?? ((int?)args.Outcome.Result?.StatusCode)?.ToString(),
                    args.RetryDelay);
                return ValueTask.CompletedTask;
            },
        });
    }
}
