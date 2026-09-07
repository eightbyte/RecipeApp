using RecipeApp.API.Enums;
using RecipeApp.API.Services;

namespace RecipeApp.Tests.Services;

/// <summary>
/// Unit tests for the pure/static helpers on RecipeScrapeService.
/// These do not require a database or real HTTP calls.
/// </summary>
public class RecipeScrapeServiceTests
{
    // ── HTML stripping ────────────────────────────────────────────────────────

    [Fact]
    public async Task StripHtml_RemovesScriptAndStyleContent()
    {
        var html = "<html><body><script>var x=1;</script><style>.a{}</style><p>Hello world</p></body></html>";
        var result = await RecipeScrapeService.StripHtmlAsync(html);
        result.Should().NotContain("var x=1");
        result.Should().NotContain(".a{}");
        result.Should().Contain("Hello world");
    }

    [Fact]
    public async Task StripHtml_RemovesNavHeaderFooterAside()
    {
        var html = "<html><body><nav>Menu</nav><header>Top</header><footer>Bottom</footer><aside>Sidebar</aside><main>Content</main></body></html>";
        var result = await RecipeScrapeService.StripHtmlAsync(html);
        result.Should().NotContain("Menu");
        result.Should().NotContain("Top");
        result.Should().NotContain("Bottom");
        result.Should().NotContain("Sidebar");
        result.Should().Contain("Content");
    }

    [Fact]
    public async Task StripHtml_RemovesDisplayNoneElements()
    {
        var html = """<html><body><div style="display:none">Hidden</div><p>Visible</p></body></html>""";
        var result = await RecipeScrapeService.StripHtmlAsync(html);
        result.Should().NotContain("Hidden");
        result.Should().Contain("Visible");
    }

    [Fact]
    public async Task StripHtml_RemovesVisibilityHiddenElements()
    {
        var html = """<html><body><div style="visibility:hidden">Invisible</div><p>Shown</p></body></html>""";
        var result = await RecipeScrapeService.StripHtmlAsync(html);
        result.Should().NotContain("Invisible");
        result.Should().Contain("Shown");
    }

    [Fact]
    public async Task StripHtml_CollapsesWhitespace()
    {
        var html = "<html><body><p>Hello    \n\n\t  World</p></body></html>";
        var result = await RecipeScrapeService.StripHtmlAsync(html);
        result.Should().NotMatchRegex(@"\s{2,}");
        result.Should().Contain("Hello World");
    }

    // ── Keyword heuristic ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("chicken breast",       IngredientCategory.MeatSeafood)]
    [InlineData("salmon fillet",        IngredientCategory.MeatSeafood)]
    [InlineData("cheddar cheese",       IngredientCategory.Dairy)]
    [InlineData("full cream milk",      IngredientCategory.Dairy)]
    [InlineData("canned chickpea",      IngredientCategory.Canned)]
    [InlineData("tinned tomatoes",      IngredientCategory.Canned)]
    [InlineData("frozen peas",          IngredientCategory.Frozen)]
    [InlineData("sourdough bread",      IngredientCategory.Bakery)]
    [InlineData("vegetable stock",      IngredientCategory.Beverages)]
    [InlineData("olive oil",            IngredientCategory.Condiments)]
    [InlineData("plain flour",          IngredientCategory.DryGoods)]
    [InlineData("brown sugar",          IngredientCategory.DryGoods)]
    [InlineData("garlic clove",         IngredientCategory.Produce)]
    [InlineData("red onion",            IngredientCategory.Produce)]
    [InlineData("xylitol crystals",     IngredientCategory.Other)]
    public void CategoriseIngredient_ReturnsExpectedCategory(string name, string expected)
    {
        var result = RecipeScrapeService.CategoriseIngredient(name);
        result.Should().Be(expected);
    }

    [Fact]
    public void CategoriseIngredient_UnknownNameReturnsOther()
    {
        var result = RecipeScrapeService.CategoriseIngredient("unknown exotic ingredient xyz");
        result.Should().Be(IngredientCategory.Other);
    }

    // ── Priority: MEAT_SEAFOOD before PRODUCE ─────────────────────────────────

    [Fact]
    public void CategoriseIngredient_MeatSeafoodTakesPriorityOverProduce()
    {
        // "chicken" is MEAT_SEAFOOD; should not fall through to PRODUCE
        var result = RecipeScrapeService.CategoriseIngredient("chicken");
        result.Should().Be(IngredientCategory.MeatSeafood);
    }
}
