namespace RecipeApp.API.Services.Llm;

/// <summary>Whether a provider can answer and, once it can, the name of the model that will.</summary>
public sealed record LlmStatus(LlmProvider Provider, LlmState State, string? Model = null);

public interface ILlmStatusProbe
{
    /// <summary>Asks the provider now, uncached, giving up after that provider's status timeout.</summary>
    Task<LlmStatus> GetStatusAsync(CancellationToken ct = default);
}
