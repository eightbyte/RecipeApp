using FluentAssertions;
using RecipeApp.API.Services.Seeding;

namespace RecipeApp.Tests.Seeding;

/// <summary>
/// Stage 5's row-level reassignment (Phase 10 D12): a listed row resolves to another catalogue
/// entry, and nothing else in the recipe changes.
/// </summary>
public class SeedIngredientRowOverridesTests
{
    // Lists one line: "1 teaspoon fresh ginger, minced" → "ginger root".
    private const string ListedSlug = "baked-tofu";
    private const string ListedLine = "1 teaspoon fresh ginger, minced";

    private static NormalisedSeedIngredient Row(string line, string name) =>
        new(name, name, 1m, "tsp", 1m, "tsp", null, line);

    private static NormalisedSeedRecipe Recipe(string slug, params NormalisedSeedIngredient[] ingredients) => new()
    {
        Slug              = slug,
        SourceUrl         = "https://www.myplate.gov/recipes/" + slug,
        ParsedFingerprint = "fingerprint",
        NormalisedAt      = DateTime.UtcNow,
        Attempts          = 1,
        Name              = "Test",
        Servings          = 4,
        Ingredients       = ingredients,
        Steps             = [new NormalisedSeedStep(1, "Cook.", [0, 1])],
    };

    [Fact]
    public void Apply_RenamesTheListedRowAndKeepsEveryOtherField()
    {
        var listed = Row(ListedLine, "ginger");
        var recipe = Recipe(ListedSlug, Row("1 block tofu", "tofu"), listed);

        var outcome = SeedIngredientRowOverrides.Apply(recipe);

        outcome.Reassigned.Should().Be(1);
        outcome.Unmatched.Should().BeEmpty();
        outcome.Recipe.Ingredients[0].Should().Be(recipe.Ingredients[0]);
        outcome.Recipe.Ingredients[1].Should().Be(listed with { Name = "ginger root" });
        outcome.Recipe.Steps.Should().Equal(recipe.Steps);
    }

    [Fact]
    public void Apply_ReassignsOneRowPerListedLine()
    {
        // A duplicate line keeps its own name: one listing reassigns one occurrence.
        var recipe = Recipe(ListedSlug, Row(ListedLine, "ginger"), Row(ListedLine, "ginger"));

        var outcome = SeedIngredientRowOverrides.Apply(recipe);

        outcome.Reassigned.Should().Be(1);
        outcome.Recipe.Ingredients.Select(row => row.Name).Should().Equal("ginger root", "ginger");
    }

    [Fact]
    public void Apply_ReportsAListedLineTheRecipeDoesNotContain()
    {
        var recipe = Recipe(ListedSlug, Row("1 block tofu", "tofu"));

        var outcome = SeedIngredientRowOverrides.Apply(recipe);

        outcome.Reassigned.Should().Be(0);
        outcome.Unmatched.Should().Equal(ListedLine);
        outcome.Recipe.Should().BeSameAs(recipe);
    }

    [Fact]
    public void Apply_LeavesAnUnlistedSlugAlone()
    {
        var recipe = Recipe("not-listed", Row(ListedLine, "ginger"));

        var outcome = SeedIngredientRowOverrides.Apply(recipe);

        outcome.Should().Be(new SeedIngredientRowOverrides.Outcome(recipe, 0, []));
    }

    [Fact]
    public void NoListedLineIsAlsoRemovedAsANonIngredientRow()
    {
        // Removal runs first, so a line in both tables would silently never be reassigned.
        foreach (var (slug, overrides) in SeedIngredientRowOverrides.BySlug)
        {
            if (!SeedNonIngredientRows.BySlug.TryGetValue(slug, out var removed)) continue;

            overrides.Select(entry => entry.SourceText).Should().NotIntersectWith(removed, $"slug {slug}");
        }
    }
}
