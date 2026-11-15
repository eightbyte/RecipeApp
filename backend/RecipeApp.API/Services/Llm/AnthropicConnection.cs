using Anthropic;
using Anthropic.Exceptions;

namespace RecipeApp.API.Services.Llm;

/// <summary>What the Claude client and its status probe share: how a client is built and which failures mean Claude cannot take a request at all.</summary>
internal static class AnthropicConnection
{
    /// <summary>Claude's own "overloaded", which the SDK treats as any other 5xx.</summary>
    private const int Overloaded = 529;

    /// <exception cref="LlmUnavailableException">No API key is configured.</exception>
    public static AnthropicClient Create(IHttpClientFactory httpClientFactory, AnthropicOptions options, int maxRetries)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new LlmUnavailableException(LlmProvider.Anthropic, LlmState.NotConfigured);

        return new AnthropicClient
        {
            ApiKey     = options.ApiKey,
            HttpClient = httpClientFactory.CreateClient(AnthropicStructuredClient.HttpClientName),
            MaxRetries = maxRetries,
            Timeout    = TimeSpan.FromSeconds(options.TimeoutSeconds),
        };
    }

    /// <summary>
    /// The state a failure proves Claude is in (401/403 unauthorized, 404 a model it does not know, 429/529 busy, any
    /// other 5xx or no answer offline), or null for a failure that is the request's own fault.
    /// </summary>
    public static LlmState? UnavailableStateOf(Exception failure) => failure switch
    {
        AnthropicUnauthorizedException or AnthropicForbiddenException => LlmState.Unauthorized,
        AnthropicNotFoundException                                    => LlmState.NotConfigured,
        AnthropicRateLimitException                                   => LlmState.Busy,
        Anthropic5xxException { StatusCode: (System.Net.HttpStatusCode)Overloaded } => LlmState.Busy,
        Anthropic5xxException                                         => LlmState.Offline,
        AnthropicIOException or HttpRequestException                  => LlmState.Offline,
        _                                                             => null,
    };
}
