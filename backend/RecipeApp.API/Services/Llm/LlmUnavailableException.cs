namespace RecipeApp.API.Services.Llm;

/// <summary>The LLM provider could not take the request at all, as opposed to giving a bad answer, so callers stop rather than retry.</summary>
public sealed class LlmUnavailableException(LlmProvider provider, LlmState state)
    : Exception($"Inference is unavailable: {state.UnavailableReason(provider)}")
{
    public LlmProvider Provider { get; } = provider;
    public LlmState State { get; } = state;

    /// <summary>The reason worded as the frontend repeats it.</summary>
    public string Reason => State.UnavailableReason(Provider);
}
