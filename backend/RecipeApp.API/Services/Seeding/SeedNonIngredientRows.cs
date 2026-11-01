namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Ingredient rows in the normalised corpus that are not ingredients (group headings and leaked notes), keyed on the verbatim
/// source line because no signal in the artefact separates them from real food and positions shift on re-normalisation.
/// </summary>
public static class SeedNonIngredientRows
{
    /// <summary>Source lines to drop, per slug. Matched exactly, case-sensitively.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> BySlug =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            // ── Group headings ───────────────────────────────────────────────
            ["apple-banana-salad-peanuts"]      = ["For the Dressing"],
            ["broccoli-and-corn-bake"]          = ["Topping"],
            ["bugs-log"]                        = ["\"Logs\"", "Spread", "\"Bugs\""],
            ["bulgur-chickpea-salad"]           = ["Dressing"],
            ["citrus-salad"]                    = ["Dressing"],
            ["cucumber-blueberry-salad"]        = ["Vinaigrette", "Salad"],
            ["frozen-banana-pops"]              = ["Other Necessary Tools/Equipment"],
            ["fruit-pizza"]                     = ["Cookie Crust", "Cheese Spread"],
            ["kale-salad-yogurt-dressing"]      = ["Dressing"],
            ["oven-baked-sweet-potato-fries"]   = ["Optional Seasonings"],
            ["spinach-salad-apples-and-raisins"] = ["Salad", "Dressing"],
            ["sweet-potato-pancakes-balsamic-maple-mushrooms"] =
                ["For Sweet Potato Pancakes", "For Balsamic Maple Mushrooms"],
            ["tomato-and-cucumber-salad"]       = ["Salad", "Dressing"],
            ["turkey-stuffed-cabbage"]          = ["Cabbages", "Final Sauce"],
            ["turnip-pancakes"]                 = ["Pancakes", "Dipping Sauce"],
            ["vegetable-and-turkey-stir-fry"]   = ["Optional Gravy"],
            ["vegetable-medley-salsa-dip"]      = ["Fresh Salsa"],
            ["vinaigrette-salad-dressing"]      = ["Basic Vinaigrette"],

            // ── Notes that leaked into the ingredient list ────
            ["crispy-walleye-patties"]          = ["Note: \"Minced\" means cut up into tiny pieces."],
            ["zesty-pasta-primavera-salad"]     =
                ["*Other types of whole grain pasta may be used in place of the whole grain rotini."],
        };

    /// <param name="Unmatched">Listed lines this recipe did not contain, which marks a stale entry.</param>
    public record Outcome(NormalisedSeedRecipe Recipe, int Removed, IReadOnlyList<string> Unmatched);

    /// <summary>
    /// Removes the listed rows and re-points every step's <c>IngredientIndexes</c>, dropping references to a removed row; run it only
    /// after the artefact has been checked against <c>parsed/</c>.
    /// </summary>
    public static Outcome Remove(NormalisedSeedRecipe recipe)
    {
        if (!BySlug.TryGetValue(recipe.Slug, out var listed))
            return new Outcome(recipe, 0, []);

        var pending  = listed.ToList();
        var newIndex = new int?[recipe.Ingredients.Count];
        var kept     = new List<NormalisedSeedIngredient>(recipe.Ingredients.Count);

        for (var index = 0; index < recipe.Ingredients.Count; index++)
        {
            var row = recipe.Ingredients[index];

            // Remove one occurrence per listed line, so a heading that matches a later real line cannot take that line with it.
            if (pending.Remove(row.SourceText)) continue;

            newIndex[index] = kept.Count;
            kept.Add(row);
        }

        if (kept.Count == recipe.Ingredients.Count)
            return new Outcome(recipe, 0, pending);

        var steps = recipe.Steps
            .Select(step => step with
            {
                IngredientIndexes = [.. step.IngredientIndexes
                    .Where(index => index >= 0 && index < newIndex.Length && newIndex[index] is not null)
                    .Select(index => newIndex[index]!.Value)],
            })
            .ToList();

        return new Outcome(
            recipe with { Ingredients = kept, Steps = steps },
            recipe.Ingredients.Count - kept.Count,
            pending);
    }
}
