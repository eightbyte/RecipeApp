namespace RecipeApp.API.Services.Llm;

/// <summary>The Model Server could not take the request at all, as opposed to giving a bad answer, so callers stop rather than retry.</summary>
public sealed class ModelServerUnavailableException(ModelServerState state)
    : Exception($"Inference is unavailable: {state.UnavailableReason()}")
{
    public ModelServerState State { get; } = state;
}
