using RecipeApp.API.Services.Llm;

namespace RecipeApp.Tests.Infrastructure;

/// <summary>An <see cref="ILlmStatusProbe"/> that reports a fixed status and counts how often it was asked, with no provider behind it.</summary>
public class StubLlmStatusProbe(LlmStatus status) : ILlmStatusProbe
{
    public StubLlmStatusProbe(LlmState state, string? model = null, LlmProvider provider = LlmProvider.ModelServer)
        : this(new LlmStatus(provider, state, model)) { }

    public int CallCount { get; private set; }

    public Task<LlmStatus> GetStatusAsync(CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult(status);
    }
}
