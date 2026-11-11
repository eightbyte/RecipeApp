using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecipeApp.API.Services;

namespace RecipeApp.Tests.Services;

/// <summary>Tests for the JSON-LD-first, headless-render-as-last-resort orchestration in <c>FetchPageAsync</c>.</summary>
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
            llm: null!,                 // not used in FetchPageAsync
            modelServer: null!,         // not used in FetchPageAsync
            db: null!,                  // not used in FetchPageAsync
            headlessRenderer: headlessRenderer,
            measurementConverter: new MeasurementConverter(Options.Create(new MeasurementOptions())),
            siteReaders: [],
            images: null!,              // only confirm stores photos
            logger: NullLogger<RecipeScrapeService>.Instance);
    }

    [Fact]
    public async Task JsonLdFound_SkipsHeadlessRenderer()
    {
        var renderer = new FakeHeadlessRenderer();
        // A very high threshold proves the skip is due to JSON-LD sufficiency, not text length.
        var svc = BuildService(RecipeJsonLdHtml, renderer,
            new RecipeScrapingOptions { MinStaticContentLength = 100_000 });

        var text = (await svc.FetchPageAsync("https://example.com/recipe", default)).Text;

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

        var text = (await svc.FetchPageAsync("https://example.com/recipe", default)).Text;

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

        var text = (await svc.FetchPageAsync("https://example.com/recipe", default)).Text;

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

        var text = (await svc.FetchPageAsync("https://example.com/recipe", default)).Text;

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

        var text = (await svc.FetchPageAsync("https://example.com/recipe", default)).Text;

        renderer.CallCount.Should().Be(1);
        text.Should().Contain("a bit of thin text");
    }

    // ── Photo ────────────────────────────────────────────────────────────────

    private const string ThinHtml = "<html><body><div id=\"app\"></div></body></html>";

    private static string WithOpenGraphImage(string html, string imageUrl) =>
        html.Replace("<html>", $"<html><head><meta property=\"og:image\" content=\"{imageUrl}\"></head>");

    private static readonly RecipeScrapingOptions FallbackOn =
        new() { MinStaticContentLength = 400, EnableHeadlessFallback = true };

    [Fact]
    public async Task StaticPage_CarriesItsPhotoResolvedAgainstThePage()
    {
        var svc = BuildService(WithOpenGraphImage(RecipeJsonLdHtml, "/img/test.jpg"), new FakeHeadlessRenderer());

        var page = await svc.FetchPageAsync("https://example.com/recipe", default);

        page.ImageUrl.Should().Be("https://example.com/img/test.jpg");
    }

    [Fact]
    public async Task RenderedPageUsed_TakesThePhotoFromTheRenderedPage()
    {
        var renderer = new FakeHeadlessRenderer(WithOpenGraphImage(RecipeJsonLdHtml, "https://cdn.example.com/rendered.jpg"));
        var svc = BuildService(WithOpenGraphImage(ThinHtml, "https://cdn.example.com/static.jpg"), renderer, FallbackOn);

        var page = await svc.FetchPageAsync("https://example.com/recipe", default);

        page.Text.Should().Contain("Recipe Name: Test Recipe");
        page.ImageUrl.Should().Be("https://cdn.example.com/rendered.jpg");
    }

    [Fact]
    public async Task RenderedPageUsedWithoutAPhoto_FallsBackToTheStaticPagesPhoto()
    {
        var renderer = new FakeHeadlessRenderer(RecipeJsonLdHtml);
        var svc = BuildService(WithOpenGraphImage(ThinHtml, "https://cdn.example.com/static.jpg"), renderer, FallbackOn);

        var page = await svc.FetchPageAsync("https://example.com/recipe", default);

        page.ImageUrl.Should().Be("https://cdn.example.com/static.jpg");
    }

    [Fact]
    public async Task StaticPageUsedWithoutAPhoto_FallsBackToTheRenderedPagesPhoto()
    {
        // The rendered page is thinner than the static one, so the static text wins.
        var staticHtml = "<html><body><p>" + string.Concat(Enumerable.Repeat("Recipe text. ", 10)) + "</p></body></html>";
        var renderer = new FakeHeadlessRenderer(WithOpenGraphImage(ThinHtml, "https://cdn.example.com/rendered.jpg"));
        var svc = BuildService(staticHtml, renderer, FallbackOn);

        var page = await svc.FetchPageAsync("https://example.com/recipe", default);

        page.Text.Should().Contain("Recipe text.");
        page.ImageUrl.Should().Be("https://cdn.example.com/rendered.jpg");
    }

    [Fact]
    public async Task PageWithoutAPhoto_StillFetches()
    {
        var page = await BuildService(RecipeJsonLdHtml, new FakeHeadlessRenderer()).FetchPageAsync("https://example.com/recipe", default);

        page.Text.Should().Contain("Recipe Name: Test Recipe");
        page.ImageUrl.Should().BeNull();
    }
}
