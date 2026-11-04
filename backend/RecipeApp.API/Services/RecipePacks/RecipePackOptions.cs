namespace RecipeApp.API.Services.RecipePacks;

/// <summary>Configuration for recipe packs.</summary>
public class RecipePackOptions
{
    public const string SectionName = "RecipePacks";

    /// <summary>The built-in sample pack a fresh database is seeded with, resolved against the content root when relative.</summary>
    public string SamplePackFilePath { get; init; } = "seed-data/sample-recipes.json";
}
