namespace RecipeApp.API.Services;

/// <summary>Renders a URL in a real browser engine and returns the fully rendered HTML, as a last-resort fallback for client-side-rendered pages.</summary>
public interface IHeadlessRenderer
{
    Task<string> RenderAsync(string url, TimeSpan timeout, CancellationToken ct);
}
