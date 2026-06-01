namespace RecipeApp.API.Services;

public class RecipeScrapingOptions
{
    public const string SectionName = "RecipeScraping";

    public string AnthropicApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "claude-sonnet-4-6";
    public int HtmlFetchTimeoutSeconds { get; init; } = 15;
    public int ClaudeTimeoutSeconds { get; init; } = 60;
    public int MaxHtmlCharacters { get; init; } = 50_000;
}
