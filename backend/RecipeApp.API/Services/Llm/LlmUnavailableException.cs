namespace RecipeApp.API.Services.Llm;

/// <summary>The LLM provider could not take the request at all, as opposed to giving a bad answer, so callers stop rather than retry.</summary>
/// <param name="providerAnswered">
/// True when the request reached the provider (a 401, a 503 while loading, a connection dropped mid-generation), so the call
/// is recorded, and false when it never did (no key, no base URL, a refused connection), which records nothing.
/// </param>
public sealed class LlmUnavailableException(LlmProvider provider, LlmState state, bool providerAnswered = false)
    : Exception($"Inference is unavailable: {state.UnavailableReason(provider)}")
{
    public LlmProvider Provider { get; } = provider;
    public LlmState State { get; } = state;
    public bool ProviderAnswered { get; } = providerAnswered;

    /// <summary>The reason worded as the frontend repeats it.</summary>
    public string Reason => State.UnavailableReason(Provider);

    /// <summary>
    /// Whether a connection failure happened after the request reached the provider, which only a refused connection or a
    /// failed name lookup did not, because a connection dropped mid-generation may already have been billed.
    /// </summary>
    internal static bool ReachedProvider(Exception connectionFailure)
    {
        for (var failure = connectionFailure; failure is not null; failure = failure.InnerException)
        {
            if (failure is HttpRequestException http)
                return http.HttpRequestError is not (HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError);
        }

        return false;
    }
}
