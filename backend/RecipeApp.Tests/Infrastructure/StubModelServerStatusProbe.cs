using RecipeApp.API.Services.Llm;

namespace RecipeApp.Tests.Infrastructure;

/// <summary>An <see cref="IModelServerStatusProbe"/> that reports a fixed status and counts how often it was asked, with no Model Server.</summary>
public class StubModelServerStatusProbe(ModelServerStatus status) : IModelServerStatusProbe
{
    public StubModelServerStatusProbe(ModelServerState state, string? model = null)
        : this(new ModelServerStatus(state, model)) { }

    public int CallCount { get; private set; }

    public Task<ModelServerStatus> GetStatusAsync(CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult(status);
    }
}
