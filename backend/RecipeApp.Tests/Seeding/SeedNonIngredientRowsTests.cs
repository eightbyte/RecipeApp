using FluentAssertions;
using RecipeApp.API.Services.Seeding;

namespace RecipeApp.Tests.Seeding;

/// <summary>
/// Stage 5's removal of group headings and leaked notes. The removal is trivial; re-pointing every
/// step's <c>IngredientIndexes</c> afterwards is not — it is the exact corruption Phase 9 §24.4.2
/// found in a hand-edited artefact, where steps above a deleted row each pointed one row off.
/// </summary>
public class SeedNonIngredientRowsTests
{
    private const string ListedSlug = "citrus-salad";   // lists one line: "Dressing"

    private static NormalisedSeedIngredient Row(string line, string? name = null) =>
        new(name ?? line.ToLowerInvariant(), line, 1m, "pcs", 1m, null, null, line);

    private static NormalisedSeedRecipe Recipe(
        string slug, NormalisedSeedIngredient[] ingredients, params int[][] stepIndexes) => new()
    {
        Slug              = slug,
        SourceUrl         = "https://www.myplate.gov/recipes/" + slug,
        ParsedFingerprint = "fingerprint",
        NormalisedAt      = DateTime.UtcNow,
        Attempts          = 1,
        Name              = "Test",
        Servings          = 4,
        Ingredients       = ingredients,
        Steps             = [.. stepIndexes.Select((indexes, i) => new NormalisedSeedStep(i + 1, $"Step {i + 1}", indexes))],
    };

    [Fact]
    public void Remove_RePointsEveryIndexAboveTheRemovedRow()
    {
        var recipe = Recipe(ListedSlug,
            [Row("1 grapefruit"), Row("1 orange"), Row("Dressing"), Row("2 tablespoons vinegar"), Row("1 tablespoon oil")],
            [0, 1],      // below the heading — unchanged
            [2, 3, 4],   // references the heading — dropped; the rest shift down by one
            [4]);

        var outcome = SeedNonIngredientRows.Remove(recipe);

        outcome.Removed.Should().Be(1);
        outcome.Unmatched.Should().BeEmpty();
        outcome.Recipe.Ingredients.Select(row => row.SourceText)
            .Should().Equal("1 grapefruit", "1 orange", "2 tablespoons vinegar", "1 tablespoon oil");

        outcome.Recipe.Steps[0].IngredientIndexes.Should().Equal(0, 1);
        outcome.Recipe.Steps[1].IngredientIndexes.Should().Equal(2, 3);
        outcome.Recipe.Steps[2].IngredientIndexes.Should().Equal(3);

        // Every index still names the row it named before — the invariant that matters.
        outcome.Recipe.Ingredients[outcome.Recipe.Steps[2].IngredientIndexes[0]].SourceText
            .Should().Be("1 tablespoon oil");
    }

    [Fact]
    public void Remove_AnUnlistedRecipe_IsReturnedUnchanged()
    {
        var recipe = Recipe("not-listed", [Row("Dressing"), Row("1 cup rice")], [0, 1]);

        var outcome = SeedNonIngredientRows.Remove(recipe);

        outcome.Recipe.Should().BeSameAs(recipe);
        outcome.Removed.Should().Be(0);
    }

    [Fact]
    public void Remove_AListedLineTheRecipeNoLongerHas_IsReportedAsStale()
    {
        // A re-normalise that changes the published line would otherwise leave an inert entry that
        // looks like it is still doing its job.
        var recipe = Recipe(ListedSlug, [Row("1 grapefruit")], [0]);

        var outcome = SeedNonIngredientRows.Remove(recipe);

        outcome.Removed.Should().Be(0);
        outcome.Unmatched.Should().Equal("Dressing");
    }

    [Fact]
    public void Remove_MatchesTheWholeLineExactly()
    {
        // "Dressing" must not take "salad dressing" with it.
        var recipe = Recipe(ListedSlug, [Row("1/4 cup salad dressing"), Row("Dressing")], [0]);

        var outcome = SeedNonIngredientRows.Remove(recipe);

        outcome.Recipe.Ingredients.Select(row => row.SourceText).Should().Equal("1/4 cup salad dressing");
    }

    [Fact]
    public void BySlug_EveryEntryIsAUsableSlugWithNonBlankLines()
    {
        SeedNonIngredientRows.BySlug.Keys.Should().OnlyContain(slug => SeedCacheStore.IsValidSlug(slug));
        SeedNonIngredientRows.BySlug.Values.Should().OnlyContain(lines =>
            lines.Count > 0 && lines.All(line => !string.IsNullOrWhiteSpace(line)));
    }
}
