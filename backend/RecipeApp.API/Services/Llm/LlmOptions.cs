namespace RecipeApp.API.Services.Llm;

public class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>Which provider Scraping uses; the seed pipeline always uses the Model Server.</summary>
    public LlmProvider Provider { get; init; } = LlmProvider.ModelServer;

    public ModelServerOptions ModelServer { get; init; } = new();
    public AnthropicOptions Anthropic { get; init; } = new();
}

/// <summary>Where the Model Server (llama.cpp's <c>llama-server</c>) listens, an empty <see cref="BaseUrl"/> meaning there is none and Scraping is unavailable.</summary>
public class ModelServerOptions
{
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Sent as a Bearer token when set, and required by the start script for any non-loopback bind.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>How long a status check waits before reporting the Model Server offline.</summary>
    public int StatusTimeoutSeconds { get; init; } = 3;

    public LlmSamplingOptions Sampling { get; init; } = new();
}

/// <summary>
/// How the decoder picks among the tokens the grammar allows, left at llama.cpp's own values because lowering the temperature
/// measurably made extraction worse: misreads are systematic, and a warmer retry is what sometimes recovers them.
/// </summary>
public class LlmSamplingOptions
{
    /// <summary>Flattening applied before sampling. Lower is more deterministic.</summary>
    public float Temperature { get; init; } = 0.75f;

    /// <summary>Keep only this many highest-probability tokens.</summary>
    public int TopK { get; init; } = 40;

    /// <summary>Keep the smallest set of tokens whose probabilities sum to this.</summary>
    public float TopP { get; init; } = 0.9f;

    /// <summary>Discard tokens below this fraction of the most likely token's probability.</summary>
    public float MinP { get; init; } = 0.1f;
}

/// <summary>The Claude API, used by Scraping when <see cref="LlmOptions.Provider"/> is <see cref="LlmProvider.Anthropic"/>; an empty <see cref="ApiKey"/> means it is not configured.</summary>
public class AnthropicOptions
{
    public string ApiKey { get; init; } = string.Empty;

    public string Model { get; init; } = "claude-opus-5-5";

    /// <summary><c>output_config.effort</c>, the only control over thinking depth, since thinking cannot be turned off on the default model.</summary>
    public string Effort { get; init; } = "low";

    /// <summary>The whole budget for one completion, the SDK's retries included.</summary>
    public int TimeoutSeconds { get; init; } = 60;

    /// <summary>How often the SDK retries a connection failure, 429 or 5xx before the provider counts as busy or offline.</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>Added to the caller's token limit, because thinking tokens count against <c>max_tokens</c>.</summary>
    public int ThinkingHeadroomTokens { get; init; } = 8000;

    /// <summary>How long a status check waits before reporting Claude unreachable.</summary>
    public int StatusTimeoutSeconds { get; init; } = 3;
}
