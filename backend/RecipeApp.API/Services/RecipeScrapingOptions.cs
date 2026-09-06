namespace RecipeApp.API.Services;

public class RecipeScrapingOptions
{
    public const string SectionName = "RecipeScraping";

    public int HtmlFetchTimeoutSeconds { get; init; } = 15;
    public int LlmTimeoutSeconds { get; init; } = 120;
    public int MaxHtmlCharacters { get; init; } = 30_000;
    public double MatchConfidenceThreshold { get; init; } = 0.8;

    /// <summary>Whether to fall back to a headless browser render when static HTML has no
    /// JSON-LD recipe markup and too little visible text to extract from.</summary>
    public bool EnableHeadlessFallback { get; init; } = true;

    /// <summary>If no JSON-LD recipe was found and the stripped visible text is shorter than
    /// this many characters, the page is assumed to be a client-rendered shell and is re-fetched
    /// with a headless browser.</summary>
    public int MinStaticContentLength { get; init; } = 400;

    public int HeadlessRenderTimeoutSeconds { get; init; } = 20;

    /// <summary>Max number of headless browser pages rendered concurrently across all requests.</summary>
    public int HeadlessMaxConcurrency { get; init; } = 2;
}
