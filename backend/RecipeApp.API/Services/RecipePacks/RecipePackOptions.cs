namespace RecipeApp.API.Services.RecipePacks;

/// <summary>Configuration for recipe packs (Phase 10 §4.4.3).</summary>
public class RecipePackOptions
{
    public const string SectionName = "RecipePacks";

    /// <summary>
    /// The built-in sample pack that a fresh database is seeded with. Relative paths resolve
    /// against the content root, like the ingredient catalogue's.
    /// </summary>
    public string SamplePackFilePath { get; init; } = "seed-data/sample-recipes.json";
}
