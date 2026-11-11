using AngleSharp.Html.Parser;
using RecipeApp.API.Services;
using RecipeApp.API.Validators;

namespace RecipeApp.Tests.Services;

public class RecipePhotoLocatorTests
{
    private static readonly Uri PageUrl = new("https://example.com/recipes/chili");

    private static string? Find(string html) =>
        RecipePhotoLocator.TryFind(new HtmlParser().ParseDocument(html), PageUrl);

    private static string PageWithRecipeImage(string imageJson, string head = "") =>
        "<html><head>" + head + "<script type=\"application/ld+json\">" +
        "{\"@type\":\"Recipe\",\"name\":\"Chili\",\"image\":" + imageJson + "}" +
        "</script></head><body></body></html>";

    [Theory]
    [InlineData("\"https://cdn.example.com/chili.jpg\"")]
    [InlineData("{\"@type\":\"ImageObject\",\"url\":\"https://cdn.example.com/chili.jpg\"}")]
    [InlineData("[\"https://cdn.example.com/chili.jpg\",\"https://cdn.example.com/other.jpg\"]")]
    [InlineData("[{\"url\":\"https://cdn.example.com/chili.jpg\"},{\"url\":\"https://cdn.example.com/other.jpg\"}]")]
    public void ReadsTheRecipeImageInEachShapeItTakes(string imageJson) =>
        Find(PageWithRecipeImage(imageJson)).Should().Be("https://cdn.example.com/chili.jpg");

    [Fact]
    public void SkipsAnUnusableImageInTheListForTheFirstUsableOne() =>
        Find(PageWithRecipeImage("[\"data:image/png;base64,iVBORw0KGgo=\",{\"url\":\"\"},\"https://cdn.example.com/chili.jpg\"]"))
            .Should().Be("https://cdn.example.com/chili.jpg");

    [Fact]
    public void ResolvesARelativeImageAgainstThePage() =>
        Find(PageWithRecipeImage("\"../images/chili.jpg\"")).Should().Be("https://example.com/images/chili.jpg");

    [Fact]
    public void FallsBackToTheOpenGraphImageWhenTheRecipeHasNone() =>
        Find("""
            <html><head>
            <meta property="og:image" content="/media/chili-og.jpg">
            <script type="application/ld+json">{"@type":"Recipe","name":"Chili"}</script>
            </head><body></body></html>
            """).Should().Be("https://example.com/media/chili-og.jpg");

    [Fact]
    public void PrefersTheRecipeImageOverTheOpenGraphImage() =>
        Find(PageWithRecipeImage("\"https://cdn.example.com/chili.jpg\"",
                head: "<meta property=\"og:image\" content=\"https://cdn.example.com/og.jpg\">"))
            .Should().Be("https://cdn.example.com/chili.jpg");

    [Fact]
    public void TreatsADataUrlAsNoPhoto() =>
        Find(PageWithRecipeImage("\"data:image/png;base64,iVBORw0KGgo=\"",
                head: "<meta property=\"og:image\" content=\"data:image/png;base64,iVBORw0KGgo=\">"))
            .Should().BeNull();

    [Fact]
    public void TreatsAnImageTooLongToConfirmAsNoPhoto() =>
        Find(PageWithRecipeImage(
                "\"https://cdn.example.com/" + new string('a', RecipeLimits.ImageUrlMaxLength) + ".jpg\""))
            .Should().BeNull("the confirm validator would reject the whole recipe over it");

    [Fact]
    public void FindsNoPhotoOnAPageWithoutOne() =>
        Find("<html><head><title>Chili</title></head><body><img src=\"/logo.png\"></body></html>")
            .Should().BeNull();
}
