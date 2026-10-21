using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecipeApp.API.Services;

namespace RecipeApp.Tests.Services;

/// <summary>
/// Unit tests for RecipeScrapeService.FetchAndStripHtmlAsync — the JSON-LD-first,
/// headless-render-as-last-resort orchestration.
/// </summary>
public class RecipeScrapeServiceFetchTests
{
    private const string RecipeJsonLdHtml = """
        <html><body><script type="application/ld+json">
        {
          "@type": "Recipe",
          "name": "Test Recipe",
          "recipeIngredient": ["1 onion"],
          "recipeInstructions": ["Dice it."]
        }
        </script></body></html>
        """;

    private static RecipeScrapeService BuildService(
        string staticHtml,
        IHeadlessRenderer headlessRenderer,
        RecipeScrapingOptions? options = null)
    {
        options ??= new RecipeScrapingOptions();
        return new RecipeScrapeService(
            httpClientFactory: new FakeHttpClientFactory(staticHtml),
            options: Options.Create(options),
            llm: null!,                 // not used in FetchAndStripHtmlAsync
            db: null!,                  // not used in FetchAndStripHtmlAsync
            headlessRenderer: headlessRenderer,
            measurementConverter: new MeasurementConverter(Options.Create(new MeasurementOptions())),
            logger: NullLogger<RecipeScrapeService>.Instance);
    }

    [Fact]
    public async Task JsonLdFound_SkipsHeadlessRenderer()
    {
        var renderer = new FakeHeadlessRenderer();
        // A very high threshold proves the skip is due to JSON-LD sufficiency, not text length.
        var svc = BuildService(RecipeJsonLdHtml, renderer,
            new RecipeScrapingOptions { MinStaticContentLength = 100_000 });

        var text = await svc.FetchAndStripHtmlAsync("https://example.com/recipe", default);

        text.Should().Contain("Recipe Name: Test Recipe");
        renderer.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task StaticTextSufficient_NoJsonLd_SkipsHeadlessRenderer()
    {
        var longVisibleText = "<html><body><p>" + string.Concat(Enumerable.Repeat("Some visible recipe text. ", 50)) + "</p></body></html>";
        var renderer = new FakeHeadlessRenderer();
        var svc = BuildService(longVisibleText, renderer,
            new RecipeScrapingOptions { MinStaticContentLength = 400 });

        var text = await svc.FetchAndStripHtmlAsync("https://example.com/recipe", default);

        text.Should().Contain("Some visible recipe text.");
        renderer.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task StaticTextThin_NoJsonLd_FallsBackToHeadlessRenderer()
    {
        var thinHtml = "<html><body><div id=\"app\"></div></body></html>";
        var renderer = new FakeHeadlessRenderer(RecipeJsonLdHtml);
        var svc = BuildService(thinHtml, renderer,
            new RecipeScrapingOptions { MinStaticContentLength = 400, EnableHeadlessFallback = true });

        var text = await svc.FetchAndStripHtmlAsync("https://example.com/recipe", default);

        renderer.CallCount.Should().Be(1);
        text.Should().Contain("Recipe Name: Test Recipe");
    }

    [Fact]
    public async Task HeadlessFallbackDisabled_NeverCallsRenderer()
    {
        var thinHtml = "<html><body><div id=\"app\"></div></body></html>";
        var renderer = new FakeHeadlessRenderer(RecipeJsonLdHtml);
        var svc = BuildService(thinHtml, renderer,
            new RecipeScrapingOptions { MinStaticContentLength = 400, EnableHeadlessFallback = false });

        var text = await svc.FetchAndStripHtmlAsync("https://example.com/recipe", default);

        renderer.CallCount.Should().Be(0);
        text.Should().NotContain("Test Recipe");
    }

    [Fact]
    public async Task HeadlessRendererThrows_FallsBackToStaticTextWithoutThrowing()
    {
        var thinHtml = "<html><body><div id=\"app\">a bit of thin text</div></body></html>";
        var renderer = new FakeHeadlessRenderer(throwOnRender: new InvalidOperationException("browser crashed"));
        var svc = BuildService(thinHtml, renderer,
            new RecipeScrapingOptions { MinStaticContentLength = 400, EnableHeadlessFallback = true });

        var text = await svc.FetchAndStripHtmlAsync("https://example.com/recipe", default);

        renderer.CallCount.Should().Be(1);
        text.Should().Contain("a bit of thin text");
    }
}
