using FluentAssertions;
using RecipeApp.API.Services.RecipePacks;

namespace RecipeApp.Tests.RecipePacks;

/// <summary>
/// Reading a recipe pack (Phase 10 §4.4.1). No database.
/// </summary>
public class RecipePackFileStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "recipeapp-pack-store-tests", Guid.NewGuid().ToString("N"));

    public RecipePackFileStoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private async Task<string> WriteJsonAsync(string json)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, json);
        return path;
    }

    private const string ValidPack = """
        {
          // Comments and trailing commas are allowed: packs are written by hand.
          "formatVersion": 1,
          "contentVersion": 7,
          "recipes": [
            {
              "name": "Toast",
              "description": null,
              "servings": 2,
              "sourceUrl": null,
              "image": null,
              "ingredients": [
                { "ingredient": "bread", "amount": 2, "unit": "pcs", "notes": null },
                { "ingredient": "salt", "amount": null, "unit": null, "notes": "to taste" },
              ],
              "steps": [ { "instruction": "Toast the bread.", "ingredients": ["bread"] } ]
            }
          ]
        }
        """;

    [Fact]
    public async Task AValidPackLoads()
    {
        var pack = await RecipePackFileStore.LoadAsync(await WriteJsonAsync(ValidPack));

        pack.ContentVersion.Should().Be(7);
        pack.Recipes.Should().ContainSingle();

        var recipe = pack.Recipes[0];
        recipe.Ingredients.Should().HaveCount(2);
        recipe.Ingredients[1].Amount.Should().BeNull();
        recipe.Ingredients[1].Notes.Should().Be("to taste");
        recipe.Steps.Single().Ingredients.Should().Equal("bread");
    }

    [Fact]
    public async Task OptionalFieldsMayBeLeftOut()
    {
        var json = """
            { "formatVersion": 1, "contentVersion": 1, "recipes": [
              { "name": "Toast", "servings": 2,
                "ingredients": [ { "ingredient": "bread", "amount": 2, "unit": "pcs" } ],
                "steps": [] } ] }
            """;

        var recipe = (await RecipePackFileStore.LoadAsync(await WriteJsonAsync(json))).Recipes.Single();

        recipe.Description.Should().BeNull();
        recipe.SourceUrl.Should().BeNull();
        recipe.Image.Should().BeNull();
        recipe.Ingredients.Single().Notes.Should().BeNull();
    }

    [Fact]
    public async Task AnOmittedAmountIsAnErrorEvenThoughNullIsAllowed()
    {
        var path = await WriteJsonAsync(ValidPack.Replace("\"amount\": null, ", ""));

        var load = () => RecipePackFileStore.LoadAsync(path);

        (await load.Should().ThrowAsync<RecipePackValidationException>())
            .Which.Message.Should().Contain("amount");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    public async Task AnyContentVersionIsAccepted(int contentVersion)
    {
        var json = ValidPack.Replace("\"contentVersion\": 7", $"\"contentVersion\": {contentVersion}");

        var pack = await RecipePackFileStore.LoadAsync(await WriteJsonAsync(json));

        pack.ContentVersion.Should().Be(contentVersion);
    }

    [Theory]
    [InlineData("\"formatVersion\": 2,")]
    [InlineData("\"formatVersion\": 0,")]
    [InlineData("")]
    public async Task AnUnknownFormatVersionIsRejected(string formatLine)
    {
        var json = ValidPack.Replace("\"formatVersion\": 1,", formatLine);

        var path = await WriteJsonAsync(json);
        var load = () => RecipePackFileStore.LoadAsync(path);

        (await load.Should().ThrowAsync<RecipePackValidationException>())
            .Which.Message.Should().Contain("format version");
    }

    [Fact]
    public async Task AMisspeltPropertyIsRejectedRatherThanIgnored()
    {
        var path = await WriteJsonAsync(ValidPack.Replace("\"image\": null", "\"imgae\": \"photo.jpg\""));

        var load = () => RecipePackFileStore.LoadAsync(path);

        (await load.Should().ThrowAsync<RecipePackValidationException>())
            .Which.Message.Should().Contain("imgae");
    }

    [Fact]
    public async Task AMissingRequiredFieldIsRejected()
    {
        var path = await WriteJsonAsync(ValidPack.Replace("\"servings\": 2,", ""));

        var load = () => RecipePackFileStore.LoadAsync(path);

        await load.Should().ThrowAsync<RecipePackValidationException>();
    }

    [Fact]
    public async Task MalformedJsonIsRejected()
    {
        var path = await WriteJsonAsync("{ \"formatVersion\": 1, ");
        var load = () => RecipePackFileStore.LoadAsync(path);

        await load.Should().ThrowAsync<RecipePackValidationException>();
    }

    [Fact]
    public async Task AMissingFileIsReportedAsMissing()
    {
        var load = () => RecipePackFileStore.LoadAsync(Path.Combine(_root, "nope.json"));

        await load.Should().ThrowAsync<FileNotFoundException>();
    }
}
