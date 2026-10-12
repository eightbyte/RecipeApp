using System.Text.Json;
using RecipeApp.API.Services.RecipePacks;

namespace RecipeApp.Tests.RecipePacks;

/// <summary>
/// Builds recipe packs and writes them to a temp directory, the way a pack author would.
/// </summary>
internal static class RecipePackFixtures
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = true,
    };

    public static RecipePackFile Pack(params RecipePackRecipe[] recipes) => new()
    {
        FormatVersion  = RecipePackFile.CurrentFormatVersion,
        ContentVersion = 1,
        Recipes        = recipes,
    };

    public static RecipePackRecipe Recipe(
        string name,
        RecipePackIngredient[] ingredients,
        RecipePackStep[]? steps = null,
        string? sourceUrl = null,
        string? image = null,
        int servings = 4) =>
        new(name, servings, ingredients, steps ?? [], $"About {name}.", sourceUrl, image);

    public static RecipePackIngredient Row(string ingredient, decimal? amount = 1, string? unit = "pcs", string? notes = null) =>
        new(ingredient, amount, unit, notes);

    public static RecipePackStep Step(string instruction, params string[] ingredients) => new(instruction, ingredients);

    /// <returns>The pack file's path.</returns>
    public static async Task<string> WriteAsync(string directory, RecipePackFile pack, string fileName = "pack.json")
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(pack, WriteOptions));
        return path;
    }

    /// <summary>Writes a small file with an image extension. The importer checks extension and size, not content.</summary>
    public static async Task<string> WriteImageAsync(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        return path;
    }
}
