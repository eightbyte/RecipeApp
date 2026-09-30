namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Ingredient rows in the normalised corpus that are not ingredients — group headings and leaked
/// notes — and the one operation that removes them before Stage 5 persists a recipe.
///
/// <para><b>Why a reviewed list rather than a rule.</b> Stage 3 drops a heading only when it is
/// bolded <i>and</i> ends in a colon (§10.2b), so colon-less headings reach Stage 4 as ingredient
/// lines, and the count invariant (§11.3) means Stage 4 must return a row for each. No signal
/// in the artefact separates them from real food: "unquantified and used by no step" finds
/// <c>Cookie Crust</c> but also <c>salt (optional)</c> and <c>cooking spray</c>, and misses
/// <c>Dressing</c>, which the model links to the steps that make it. Nor can the skip key on a
/// failed catalogue lookup (§24.4.1): <c>Cookie Crust</c>, <c>Dressing</c> and <c>Salad</c> all
/// <i>resolve</i>, and would enter the database as ingredients with no error at all.</para>
///
/// <para><b>Keyed on the verbatim source line, never on a position.</b> Positions shift whenever a
/// recipe is re-normalised with a different row count; the published line does not. An entry that
/// matches nothing in its recipe is reported by the persister, so a stale entry is visible rather
/// than silently inert.</para>
///
/// <para>Drafted from every unquantified row whose line reads as a title or a note, then read in
/// context against the surrounding ingredient list (2026-09-28). <c>Toppings of your choice</c>
/// was considered and kept — it is something to buy.</para>
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

            // ── Notes that leaked into the ingredient list (§10.3 hazard 5) ───
            ["crispy-walleye-patties"]          = ["Note: \"Minced\" means cut up into tiny pieces."],
            ["zesty-pasta-primavera-salad"]     =
                ["*Other types of whole grain pasta may be used in place of the whole grain rotini."],
        };

    /// <param name="Recipe">The recipe without the listed rows, every step index re-pointed.</param>
    /// <param name="Removed">Rows dropped.</param>
    /// <param name="Unmatched">Listed lines this recipe did not contain — a stale entry.</param>
    public record Outcome(NormalisedSeedRecipe Recipe, int Removed, IReadOnlyList<string> Unmatched);

    /// <summary>
    /// Removes the listed rows and re-points every step's <c>IngredientIndexes</c>.
    ///
    /// <para><b>The re-pointing is the whole difficulty.</b> Indexes are positional, so deleting row
    /// <i>n</i> silently shifts every index above it — exactly the corruption §24.4.2 found in a
    /// hand-edited artefact, where steps 5–7 each pointed one row off and only the overrun past the
    /// end was detectable. A reference to a dropped row is removed rather than re-pointed: a step
    /// that "uses" <c>Dressing</c> uses the rows listed under it, which it already references.</para>
    ///
    /// <para>Run this only after the artefact has been checked against <c>parsed/</c>: the count
    /// invariant holds for the rows as Stage 4 wrote them, not after they are removed.</para>
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

            // Remove one occurrence per listed line, so a recipe with two "Dressing" headings would
            // need listing twice — a heading that happens to match a later real line cannot take
            // that line with it.
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
