namespace RecipeApp.API.Data;

/// <summary>
/// <see cref="Models.SeedLedgerEntry.Key"/> values, one per ledger-guarded first-run step
/// (Phase 10 §4.2). Densities have no key: that step fills nulls only and runs on every start.
///
/// <para>These strings are stored in existing databases. Renaming one makes its step look
/// un-run, and it would then run again on the next start.</para>
/// </summary>
public static class SeedLedgerKeys
{
    public const string IngredientCatalogue = "ingredient-catalogue";
    public const string SampleRecipes       = "sample-recipes";

    public static readonly IReadOnlyList<string> All = [IngredientCatalogue, SampleRecipes];
}
