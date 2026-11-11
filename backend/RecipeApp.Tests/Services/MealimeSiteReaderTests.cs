using AngleSharp.Html.Parser;
using RecipeApp.API.Services;

namespace RecipeApp.Tests.Services;

/// <summary>How the Mealime Site Reader reads each step's own ingredient lines from the page's <c>__NEXT_DATA__</c>.</summary>
public class MealimeSiteReaderTests
{
    private static async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>?> ReadAsync(string html)
    {
        var doc = await new HtmlParser().ParseDocumentAsync(html);
        return new MealimeSiteReader().ReadStepLines(doc);
    }

    [Fact]
    public async Task ReadsEachStepsLinesUnderItsStepNumber_SkippingStepsWithNone()
    {
        var lines = await ReadAsync(MealimePageFixture.Html);

        lines.Should().NotBeNull();
        lines!.Keys.Should().BeEquivalentTo([2, 3]);
        lines[2].Should().Equal("1 can chickpeas", "1 tbsp olive oil", "½ tsp salt");
        lines[3].Should().Equal("½ tsp salt");
    }

    [Theory]
    [InlineData("<html><body><p>No page data here.</p></body></html>")]
    [InlineData("""<html><body><script id="__NEXT_DATA__" type="application/json">{ not json</script></body></html>""")]
    [InlineData("""<html><body><script id="__NEXT_DATA__" type="application/json">{"props":{"pageProps":{}}}</script></body></html>""")]
    [InlineData("""<html><body><script id="__NEXT_DATA__" type="application/json">{"props":{"pageProps":{"publishedRecipe":{"instructions":{"a":1}}}}}</script></body></html>""")]
    [InlineData("""<html><body><script id="__NEXT_DATA__" type="application/json">{"props":{"pageProps":{"publishedRecipe":{"instructions":[{"secondary_message":3}]}}}}</script></body></html>""")]
    public async Task PageDataInAnUnexpectedShape_ReadsNothing(string html)
    {
        var lines = await ReadAsync(html);

        lines.Should().BeNull();
    }
}
