namespace RecipeApp.API.Data;

/// <summary><see cref="Models.SeedLedgerEntry.Key"/> values, one per ledger-guarded first-run step, stored in existing databases so renaming one would make its step run again.</summary>
public static class SeedLedgerKeys
{
    public const string IngredientCatalogue = "ingredient-catalogue";
    public const string SampleRecipes       = "sample-recipes";

    public static readonly IReadOnlyList<string> All = [IngredientCatalogue, SampleRecipes];
}
