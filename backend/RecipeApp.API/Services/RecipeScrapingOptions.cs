namespace RecipeApp.API.Services;

public class RecipeScrapingOptions
{
    public const string SectionName = "RecipeScraping";

    public int HtmlFetchTimeoutSeconds { get; init; } = 15;
    public int LlmTimeoutSeconds { get; init; } = 120;
    public int MaxHtmlCharacters { get; init; } = 30_000;
    public double MatchConfidenceThreshold { get; init; } = 0.8;
}
