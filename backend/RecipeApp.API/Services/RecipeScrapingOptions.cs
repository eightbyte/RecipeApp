namespace RecipeApp.API.Services;

public class RecipeScrapingOptions
{
    public const string SectionName = "RecipeScraping";

    public int HtmlFetchTimeoutSeconds { get; init; } = 15;

    /// <summary>How long confirm waits for the recipe's photo before saving the recipe without it.</summary>
    public int ImageFetchTimeoutSeconds { get; init; } = 15;
    public int LlmTimeoutSeconds { get; init; } = 120;
    public int MaxHtmlCharacters { get; init; } = 30_000;
    public double MatchConfidenceThreshold { get; init; } = 0.8;

    /// <summary>Whether to fall back to a headless browser render when static HTML has no JSON-LD recipe markup and too little visible text.</summary>
    public bool EnableHeadlessFallback { get; init; } = true;

    /// <summary>Visible-text length below which a page with no JSON-LD recipe is assumed to be a client-rendered shell and re-fetched with a headless browser.</summary>
    public int MinStaticContentLength { get; init; } = 400;

    public int HeadlessRenderTimeoutSeconds { get; init; } = 20;

    /// <summary>Max number of headless browser pages rendered concurrently across all requests.</summary>
    public int HeadlessMaxConcurrency { get; init; } = 2;
}
