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

    // ── Unit conversion ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("oz",           1.0,  28.350, "g")]
    [InlineData("ounce",        2.0,  56.699, "g")]
    [InlineData("ounces",       3.0,  85.049, "g")]
    [InlineData("lb",           1.0, 453.592, "g")]
    [InlineData("lbs",          2.0, 907.184, "g")]
    [InlineData("pound",        1.0, 453.592, "g")]
    [InlineData("pounds",       1.0, 453.592, "g")]
    [InlineData("fl oz",        1.0,  29.574, "ml")]
    [InlineData("fluid oz",     1.0,  29.574, "ml")]
    [InlineData("fluid ounce",  1.0,  29.574, "ml")]
    [InlineData("cup",          1.0, 240.0,   "ml")]
    [InlineData("cups",         2.0, 480.0,   "ml")]
    [InlineData("pt",           1.0, 473.176, "ml")]
    [InlineData("pint",         1.0, 473.176, "ml")]
    [InlineData("pints",        1.0, 473.176, "ml")]
    [InlineData("qt",           1.0, 946.353, "ml")]
    [InlineData("quart",        1.0, 946.353, "ml")]
    [InlineData("quarts",       1.0, 946.353, "ml")]
    [InlineData("gal",          1.0,   3.785, "L")]
    [InlineData("gallon",       1.0,   3.785, "L")]
    [InlineData("gallons",      2.0,   7.571, "L")]
    public void ConvertUnit_ConvertsImperialToMetric(string unit, double input, double expectedApprox, string expectedUnit)
    {
        var (amount, outUnit) = RecipeScrapeService.ConvertUnit(input, unit);
        outUnit.Should().Be(expectedUnit);
        ((double)amount).Should().BeApproximately(expectedApprox, 0.01);
    }

    [Theory]
    [InlineData("g")]
    [InlineData("kg")]
    [InlineData("ml")]
    [InlineData("L")]
    [InlineData("tsp")]
    [InlineData("tbsp")]
    [InlineData("pcs")]
    [InlineData("piece")]
    [InlineData("pieces")]
    [InlineData("clove")]
    [InlineData("pinch")]
    public void ConvertUnit_MetricUnitsPassThrough(string unit)
    {
        var (_, outUnit) = RecipeScrapeService.ConvertUnit(100.0, unit);
        outUnit.Should().Be(unit);
    }

    [Fact]
    public void ConvertUnit_RoundsToThreeDecimalPlaces()
    {
        var (amount, _) = RecipeScrapeService.ConvertUnit(1.0, "oz");
        var decimals = amount.ToString().Contains('.') ? amount.ToString().Split('.')[1].Length : 0;
        decimals.Should().BeLessThanOrEqualTo(3);
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
