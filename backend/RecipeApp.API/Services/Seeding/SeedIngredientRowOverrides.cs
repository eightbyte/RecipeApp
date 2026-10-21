namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// MyPlate ingredient rows that resolve to a different catalogue entry than their normalised name
/// says, and the one operation that reassigns them before Stage 5 resolves a recipe (Phase 10
/// §4.7, D12).
///
/// <para><b>Why rows and not the catalogue.</b> Catalogue curation works on names: a fold moves
/// every row carrying a name. But the Phase 9 normaliser sometimes gave a row a name less specific
/// than its own line. <c>1/2 cup cheese, reduced-fat sharp cheddar, shredded</c> was filed as
/// <c>cheese</c>, and <c>1 tablespoon ginger, minced</c> as the ground spice. No fold can move
/// those rows without moving the generic ones with them. This table moves them one at a time.</para>
///
/// <para><b>Why not edit <c>normalised/</c>.</b> It is a gitignored cache that
/// <c>--normalise --force</c> rewrites, so an edit there would be lost. This table is reviewed
/// source, like <see cref="SeedNonIngredientRows"/>.</para>
///
/// <para><b>Keyed on the verbatim source line, never on a position</b>, for the same reason as
/// <see cref="SeedNonIngredientRows"/>. An entry that matches nothing in its recipe is reported
/// by the persister, so a stale entry is visible rather than silently inert. Every target must be
/// an entry name in the shipped catalogue; a test enforces it.</para>
///
/// <para>Only MyPlate imports read this. The shipped catalogue and recipe packs are unaffected.</para>
/// </summary>
public static class SeedIngredientRowOverrides
{
    /// <param name="SourceText">The row's verbatim source line. Matched exactly, case-sensitively.</param>
    /// <param name="CatalogueName">The catalogue entry name the row resolves to instead.</param>
    public record RowOverride(string SourceText, string CatalogueName);

    /// <summary>Rows to reassign, per slug.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<RowOverride>> BySlug =
        new Dictionary<string, IReadOnlyList<RowOverride>>(StringComparer.Ordinal)
        {
            // ── Cheese lines that name a type, filed under generic "cheese" ──
            ["avocado-pita-pockets"] = [new("2 tablespoons crumbled Cotija or Feta cheese", "feta cheese")],
            ["black-bean-quesadillas"] = [new("2 cups shredded reduced-fat Colby & Monterey Jack cheese", "monterey jack cheese")],
            ["four-layer-casserole"] = [new("1/4 cup cheese, low-fat shredded (cheddar or colby jack)", "cheddar cheese")],
            ["grilled-cheese-peaches"] = [new("8 slices reduced fat cheddar cheese", "cheddar cheese")],
            ["leftover-turkey-casserole"] = [new("1/2 cup reduced-fat shredded cheddar cheese", "cheddar cheese")],
            ["main-dish-salad"] = [new("6 ounces cheese, grated low-fat cheddar", "cheddar cheese")],
            ["polenta-pepper-and-cheese"] = [new("6 ounces cheese, cheddar, reduced fat, shredded", "cheddar cheese")],
            ["potato-spinach-casserole"] =
            [
                new("1/2 cup cheese, reduced-fat sharp cheddar, shredded", "cheddar cheese"),
                new("1/2 cup cheese, reduced fat Monterey Jack, shredded", "monterey jack cheese"),
            ],
            ["pupusas-revueltas"] = [new("1/2 pound cheese, part-skim mozzarella, grated", "mozzarella")],

            // ── Fresh ginger, filed under the ground spice ──
            ["baked-tofu"] = [new("1 teaspoon fresh ginger, minced", "ginger root")],
            ["chicken-stir-fry"] = [new("1 tablespoon ginger, finely chopped (optional)", "ginger root")],
            ["peanut-stew"] = [new("1 tablespoon ginger, minced", "ginger root")],
            ["spicy-southern-barbecued-chicken"] = [new("1/8 teaspoon ginger (grated)", "ginger root")],
            ["tasty-chicken-and-veggie-packets"] = [new("ginger (minced, optional)", "ginger root")],
            ["tasty-fish-and-veggie-packets"] = [new("ginger (minced, optional)", "ginger root")],

            // ── Corn that is frozen, fresh or cream-style, filed under canned corn or frozen corn ──
            ["beef-and-vegetables"] = [new("1 1/2 cups fresh cooked corn kernels (or 1 can, about 15 ounces, low-sodium corn)", "corn cob")],
            ["breakfast-burrito-salsa"] = [new("1/8 cup frozen corn (2 tablespoons)", "frozen corn")],
            ["cheese-and-corn-chowder"] = [new("1 can (14.75 ounce) cream-style corn, low-sodium", "cream-style corn")],
            ["chicken-pasta-salad"] = [new("1/2 cup canned corn kernels, low-sodium, drained", "canned corn")],
            ["corn-and-green-chili-salad"] = [new("2 cups frozen corn, thawed", "frozen corn")],
            ["corn-salad-fat-free-ranch-dressing"] = [new("2 cups whole kernel corn, fresh or frozen (cooked and drained)", "corn cob")],
            ["corn-salad-fresh-basil-and-lemon"] = [new("6 ears of corn (about 3 cups)", "corn cob")],
            ["farmers-market-salsa"] = [new("1/2 cup fresh corn, cooked (or frozen)", "corn cob")],
            ["quick-black-bean-salad"] = [new("1 1/2 10-ounce packages frozen corn", "frozen corn")],

            // ── Whole-wheat tortillas, filed under the generic tortilla ──
            ["baked-tortilla-chips"] = [new("3 whole wheat flour tortillas, 10\" across", "whole wheat tortillas")],
            ["black-bean-and-sweet-potato-quesadillas"] = [new("4 whole wheat tortillas", "whole wheat tortillas")],
            ["california-avocado-super-summer-wrap"] = [new("4 8-inch whole wheat tortillas", "whole wheat tortillas")],
            ["peanut-butter-and-apple-wraps"] = [new("1 (7-inch across) whole wheat tortilla", "whole wheat tortillas")],
            ["red-bean-quesadilla"] = [new("4 8-inch whole wheat tortilla", "whole wheat tortillas")],
            ["surfs-tacos"] = [new("12 whole grain tortillas or hard taco shells (10 inches across)", "whole wheat tortillas")],

            // ── Dried basil and parsley, filed under the fresh herb. Dried is named first on each line ──
            ["chicken-rice-and-fruit-salad"] = [new("2 teaspoons parsley, dried (or 2 Tablespoons fresh, finely chopped)", "dried parsley")],
            ["daves-herb-stuffed-mushrooms"] = [new("1/2 teaspoon dried parsley", "dried parsley")],
            ["eggplant-and-pepper-dip"] = [new("1 teaspoon dried basil", "dried basil")],
            ["grandmas-stuffing"] = [new("1 teaspoon parsley, dried (or 1 Tbsp fresh parsley chopped)", "dried parsley")],
            ["hearty-vegetable-salad-herb-dressing"] = [new("dried basil (1/2 teaspoon, optional)", "dried basil")],
            ["herb-broiled-beef"] = [new("1/4 teaspoon basil, dried (or thyme)", "dried basil")],
            ["herb-potato-salad"] = [new("1/2 teaspoon basil (dried)", "dried basil")],
            ["herbed-dip"] = [new("1 teaspoon parsley (dried)", "dried parsley")],
            ["homemade-ranch-dressing"] = [new("1 tablespoon dried parsley", "dried parsley")],
            ["hot-n-spicy-seasoning"] = [new("1 tablespoon basil (dried)", "dried basil")],
            ["pear-quesadillas"] = [new("1 tablespoon dried basil", "dried basil")],
            ["spring-vegetable-soup"] = [new("2 teaspoons dried basil", "dried basil")],
            ["squash-soup"] = [new("1 1/2 tablespoons dried basil", "dried basil")],
            ["squash-squares"] = [new("1 teaspoon dried parsley (or 2 tablespoons chopped fresh parsley)", "dried parsley")],
            ["stuffed-tomatoes"] =
            [
                new("2 teaspoons dried basil", "dried basil"),
                new("2 teaspoons dried parsley", "dried parsley"),
            ],
            ["summer-squash-squares"] = [new("1 teaspoon dried parsley (or 2 tablespoons chopped fresh parsley)", "dried parsley")],
            ["tartar-sauce"] = [new("1 teaspoon dried parsley (or 1 tablespoon chopped fresh parsley)", "dried parsley")],
            ["tomato-basil-soup"] = [new("1 teaspoon basil (dried)", "dried basil")],
            ["turkey-burgers"] = [new("1/4 teaspoon parsley, dried (or fresh chopped)", "dried parsley")],
        };

    /// <param name="Recipe">The recipe with the listed rows renamed to their catalogue entry.</param>
    /// <param name="Reassigned">Rows renamed.</param>
    /// <param name="Unmatched">Listed lines this recipe did not contain — a stale entry.</param>
    public record Outcome(NormalisedSeedRecipe Recipe, int Reassigned, IReadOnlyList<string> Unmatched);

    /// <summary>
    /// Renames each listed row to its catalogue entry. Positions are unchanged, so step indexes
    /// need no re-pointing.
    ///
    /// <para>Run after <see cref="SeedNonIngredientRows.Remove"/>, so a heading can never be
    /// reassigned instead of removed.</para>
    /// </summary>
    public static Outcome Apply(NormalisedSeedRecipe recipe)
    {
        if (!BySlug.TryGetValue(recipe.Slug, out var listed))
            return new Outcome(recipe, 0, []);

        var pending    = listed.ToList();
        var reassigned = 0;
        var rows       = new List<NormalisedSeedIngredient>(recipe.Ingredients.Count);

        foreach (var row in recipe.Ingredients)
        {
            // One override per occurrence, as in SeedNonIngredientRows: a line listed once
            // reassigns one row, so a duplicate line elsewhere in the recipe keeps its own name.
            var match = pending.FindIndex(entry => entry.SourceText == row.SourceText);
            if (match < 0)
            {
                rows.Add(row);
                continue;
            }

            rows.Add(row with { Name = pending[match].CatalogueName });
            pending.RemoveAt(match);
            reassigned++;
        }

        return new Outcome(
            reassigned == 0 ? recipe : recipe with { Ingredients = rows },
            reassigned,
            [.. pending.Select(entry => entry.SourceText)]);
    }
}
