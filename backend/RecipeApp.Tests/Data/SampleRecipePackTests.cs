using FluentAssertions;
using RecipeApp.API.DTOs.Recipes;
using RecipeApp.API.Enums;
using RecipeApp.API.Services.RecipePacks;
using RecipeApp.API.Services.Seeding;
using RecipeApp.API.Validators;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Data;

/// <summary>
/// The shipped sample pack, <c>seed-data/sample-recipes.json</c>, checked as an artefact against the
/// shipped catalogue (Phase 10 §4.5, §6). No database: names resolve the way the importer resolves
/// them, by entry name and then alias.
/// </summary>
public class SampleRecipePackTests
{
    private const int ExpectedRecipeCount = 12;

    /// <summary>Every aisle a shopper walks for food. Non-food aisles and OTHER are exempt (§4.5).</summary>
    private static readonly string[] FoodAisleCategories =
    [
        IngredientCategory.Produce, IngredientCategory.MeatSeafood, IngredientCategory.Dairy,
        IngredientCategory.Canned, IngredientCategory.SoupsBroth, IngredientCategory.Frozen,
        IngredientCategory.DryGoods, IngredientCategory.GrainsRice, IngredientCategory.PastaSauces,
        IngredientCategory.BakingSpices, IngredientCategory.Bakery, IngredientCategory.Condiments,
        IngredientCategory.JamNutButter,
    ];

    /// <summary>Entries that must each appear in at least two recipes, so a plan shows consolidation.</summary>
    private static readonly string[] SharedEntryNames = ["onion", "garlic", "olive oil", "bell pepper", "rice", "egg"];

    private static readonly Lazy<RecipePackFile> SamplePack = new(() =>
        RecipePackFileStore.LoadAsync(RepositoryPaths.ShippedSamplePackPath).GetAwaiter().GetResult());

    private static readonly Lazy<IReadOnlyDictionary<string, IngredientCatalogueEntry>> EntryByName = new(() =>
    {
        var json      = File.ReadAllText(RepositoryPaths.ShippedCataloguePath);
        var catalogue = System.Text.Json.JsonSerializer.Deserialize<IngredientCatalogueFile>(
            json, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })!;

        var byName = new Dictionary<string, IngredientCatalogueEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalogue.Entries) byName[entry.Name] = entry;
        foreach (var entry in catalogue.Entries)
            foreach (var alias in entry.Aliases) byName.TryAdd(alias, entry);
        return byName;
    });

    private static IngredientCatalogueEntry? Resolve(string name) =>
        EntryByName.Value.GetValueOrDefault(name.Trim().ToLowerInvariant());

    /// <summary>Each recipe's rows, resolved. Only meaningful once <see cref="EveryIngredientResolves"/> passes.</summary>
    private static IEnumerable<(RecipePackRecipe Recipe, RecipePackIngredient Row, IngredientCatalogueEntry Entry)> ResolvedRows() =>
        from recipe in SamplePack.Value.Recipes
        from row in recipe.Ingredients
        let entry = Resolve(row.Ingredient)
        where entry is not null
        select (recipe, row, entry);

    [Fact]
    public void ThePackHoldsTheTwelveSampleRecipes()
    {
        var recipes = SamplePack.Value.Recipes;

        recipes.Should().HaveCount(ExpectedRecipeCount);
        recipes.Select(recipe => recipe.Name).Should().OnlyHaveUniqueItems();
        recipes.Should().OnlyContain(recipe => recipe.Image == null, "the samples ship without images");
        recipes.Should().OnlyContain(recipe => recipe.SourceUrl == null, "they are original, so they have no source");
    }

    [Fact]
    public void EveryIngredientResolves()
    {
        var unresolved =
            from recipe in SamplePack.Value.Recipes
            from row in recipe.Ingredients
            where Resolve(row.Ingredient) is null
            select $"{recipe.Name}: {row.Ingredient}";

        unresolved.Should().BeEmpty("every sample ingredient must be a shipped catalogue name or alias");
    }

    [Fact]
    public void EveryStepNamesAnIngredientItsRecipeLists()
    {
        var problems = new List<string>();
        foreach (var recipe in SamplePack.Value.Recipes)
        {
            var listed = recipe.Ingredients.Select(row => Resolve(row.Ingredient)?.Name).ToHashSet();
            for (var index = 0; index < recipe.Steps.Count; index++)
                foreach (var name in recipe.Steps[index].Ingredients)
                    if (Resolve(name) is not { } entry || !listed.Contains(entry.Name))
                        problems.Add($"{recipe.Name} step {index + 1}: '{name}'");
        }

        problems.Should().BeEmpty();
    }

    [Fact]
    public void EveryRecipePassesTheCreateRecipeValidator()
    {
        // Resolution is checked above; the validator only needs a non-empty id per row.
        var validator = new CreateRecipeValidator();

        foreach (var recipe in SamplePack.Value.Recipes)
        {
            var rows = recipe.Ingredients
                .Select((row, index) => new RecipeIngredientRequest(Guid.NewGuid(), row.Amount, row.Unit, row.Notes, index))
                .ToList();
            var steps = recipe.Steps
                .Select((step, index) => new RecipeStepRequest(index + 1, step.Instruction, []))
                .ToList();

            var result = validator.Validate(new CreateRecipeRequest(
                recipe.Name, recipe.Description, recipe.SourceUrl, recipe.Servings, rows, steps));

            result.Errors.Select(error => $"{recipe.Name}: {error.PropertyName}: {error.ErrorMessage}")
                .Should().BeEmpty();
        }
    }

    // ── §4.5 feature coverage ─────────────────────────────────────────────────

    [Fact]
    public void SharedIngredientsAppearInAtLeastTwoRecipes()
    {
        var recipesByEntry = ResolvedRows()
            .GroupBy(row => row.Entry.Name)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Recipe.Name).Distinct().Count());

        foreach (var name in SharedEntryNames)
            recipesByEntry.GetValueOrDefault(name).Should().BeGreaterThanOrEqualTo(2, $"'{name}' is a shared ingredient");
    }

    [Fact]
    public void OneIngredientIsMeasuredInDifferentUnitsOfOneDimension()
    {
        // Olive oil appears in tbsp and in ml, so consolidation has to merge across units.
        var mixed = ResolvedRows()
            .Where(row => row.Row.Unit is not null)
            .GroupBy(row => (row.Entry.Name, MeasurementUnit.Table[row.Row.Unit!].Dimension))
            .Where(group => group.Select(row => row.Row.Unit).Distinct().Count() > 1)
            .Select(group => group.Key.Name);

        mixed.Should().Contain("olive oil");
    }

    [Fact]
    public void AtLeastOneIngredientIsUnquantified()
    {
        ResolvedRows().Should().Contain(row => row.Row.Amount == null && row.Row.Unit == null);
    }

    [Fact]
    public void EveryFoodAisleIsUsed()
    {
        var used = ResolvedRows().Select(row => row.Entry.Category).ToHashSet();

        FoodAisleCategories.Where(category => !used.Contains(category)).Should().BeEmpty();
    }

    [Fact]
    public void ACupOfFlourIsUsed()
    {
        // The pancakes' cup of flour exercises density-based consolidation.
        ResolvedRows().Should().Contain(row => row.Entry.Name == "all-purpose flour" && row.Row.Unit == MeasurementUnit.Cup);
    }

    // ── Shipping (§4.6) ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("ingredient-catalogue.json")]
    [InlineData("sample-recipes.json")]
    public void BothArtefactsAreInTheBuildOutput(string fileName)
    {
        File.Exists(Path.Combine(AppContext.BaseDirectory, "seed-data", fileName)).Should().BeTrue();
    }
}
