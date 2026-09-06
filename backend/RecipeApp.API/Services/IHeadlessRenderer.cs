namespace RecipeApp.API.Services;

/// <summary>
/// Renders a URL in a real browser engine and returns the fully rendered HTML (post-JavaScript).
/// Used as a last-resort fallback when a page's server-rendered HTML has neither JSON-LD recipe
/// markup nor enough visible text to extract from (e.g. a client-side-rendered SPA).
/// </summary>
public interface IHeadlessRenderer
{
    Task<string> RenderAsync(string url, TimeSpan timeout, CancellationToken ct);
}
