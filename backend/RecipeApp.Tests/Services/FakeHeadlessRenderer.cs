using RecipeApp.API.Services;

namespace RecipeApp.Tests.Services;

/// <summary>Returns a canned HTML string (or throws) instead of launching a real browser.</summary>
public class FakeHeadlessRenderer(string? html = null, Exception? throwOnRender = null) : IHeadlessRenderer
{
    public int CallCount { get; private set; }

    public Task<string> RenderAsync(string url, TimeSpan timeout, CancellationToken ct)
    {
        CallCount++;
        if (throwOnRender is not null) throw throwOnRender;
        return Task.FromResult(html ?? string.Empty);
    }
}
