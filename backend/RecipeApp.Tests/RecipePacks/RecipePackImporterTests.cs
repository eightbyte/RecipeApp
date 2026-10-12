using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;
using RecipeApp.API.Services.RecipePacks;
using RecipeApp.Tests.Helpers;
using RecipeApp.Tests.Infrastructure;
using static RecipeApp.Tests.RecipePacks.RecipePackFixtures;

namespace RecipeApp.Tests.RecipePacks;

/// <summary>
/// Recipe-pack import against a real Postgres (Phase 10 §4.4.2), through the app's own DI graph so
/// the validator, <c>RecipeService</c> and image storage are the real ones. Image storage is
/// redirected to a temp directory.
/// </summary>
[Collection("Database")]
public class RecipePackImporterTests(DatabaseFixture db) : IAsyncLifetime
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "recipeapp-pack-import-tests", Guid.NewGuid().ToString("N"));

    private WebApplicationFactory<Program> _factory = null!;

    private string PackDirectory => Path.Combine(_root, "pack");
    private string ImagesRoot    => Path.Combine(_root, "uploads");

    public async ValueTask InitializeAsync()
    {
        await db.TruncateAllAsync();

        _factory = db.Factory.WithWebHostBuilder(builder => builder
            .UseSetting("ImageStorage:BasePath", ImagesRoot));

        await CatalogueAsync(
            ("onion",     IngredientCategory.Produce,      []),
            ("garlic",    IngredientCategory.Produce,      []),
            ("olive oil", IngredientCategory.Condiments,   ["extra virgin olive oil", "evoo"]),
            ("salt",      IngredientCategory.BakingSpices, []),
            ("rice",      IngredientCategory.GrainsRice,   []));
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task CatalogueAsync(params (string Name, string Category, string[] Aliases)[] entries)
    {
        await using var context = db.CreateDbContext();
        foreach (var (name, category, aliases) in entries)
            context.Ingredients.Add(new Ingredient
            {
                Id          = Guid.NewGuid(),
                Name        = name,
                DisplayName = name,
                Category    = category,
                Aliases     = aliases,
            });
        await context.SaveChangesAsync();
    }

    private async Task<RecipePackImportResult> ImportAsync(
        RecipePackFile pack, RecipePackImportSettings? settings = null)
    {
        using var scope = _factory.Services.CreateScope();
        var importer = scope.ServiceProvider.GetRequiredService<RecipePackImporter>();
        return await importer.ImportAsync(pack, PackDirectory, settings ?? new RecipePackImportSettings());
    }

    private async Task<List<Recipe>> LoadRecipesAsync()
    {
        await using var context = db.CreateDbContext();
        return await context.Recipes
            .Include(recipe => recipe.Ingredients).ThenInclude(row => row.Ingredient)
            .Include(recipe => recipe.Steps).ThenInclude(step => step.StepIngredients)
            .AsNoTracking()
            .OrderBy(recipe => recipe.Name)
            .ToListAsync();
    }

    private async Task<int> RecipeCountAsync()
    {
        await using var context = db.CreateDbContext();
        return await context.Recipes.CountAsync();
    }

    private string[] StoredImages() =>
        Directory.Exists(ImagesRoot) ? Directory.GetFiles(ImagesRoot) : [];

    private static RecipePackRecipe Simple(string name, string? sourceUrl = null) =>
        Recipe(name, [Row("onion")], [Step("Chop the onion.", "onion")], sourceUrl);

    // ── A valid pack ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AValidPackImportsEveryRecipe()
    {
        var result = await ImportAsync(Pack(Simple("Alpha"), Simple("Beta"), Simple("Gamma")));

        result.Succeeded.Should().BeTrue();
        result.Imported.Should().HaveCount(3);
        (await LoadRecipesAsync()).Select(recipe => recipe.Name).Should().Equal("Alpha", "Beta", "Gamma");
    }

    [Fact]
    public async Task FieldsAndMeasurementsAreStoredAsStated()
    {
        var pack = Pack(Recipe("Rice Pilaf",
            [
                Row("rice", 200, "g"),
                Row("olive oil", 2, "tbsp", notes: "plus extra"),
                Row("salt", null, null, notes: "to taste"),
            ],
            servings: 6));

        (await ImportAsync(pack)).Succeeded.Should().BeTrue();

        var recipe = (await LoadRecipesAsync()).Single();
        recipe.Servings.Should().Be(6);
        recipe.Description.Should().Be("About Rice Pilaf.");
        recipe.SourceUrl.Should().BeNull();

        var rows = recipe.Ingredients.OrderBy(row => row.DisplayOrder).ToList();
        rows.Select(row => row.Ingredient.Name).Should().Equal("rice", "olive oil", "salt");
        rows[0].Amount.Should().Be(200);
        rows[0].Unit.Should().Be(MeasurementUnit.Gram);
        rows[1].Notes.Should().Be("plus extra");
        rows[2].Amount.Should().BeNull();
        rows[2].Unit.Should().BeNull();
        rows.Should().OnlyContain(row => row.SourceAmount == null && row.SourceUnit == null);
    }

    [Fact]
    public async Task StepsLinkTheRowsTheyName()
    {
        // Olive oil is listed twice (once by alias) and the second step names it by another alias:
        // both rows resolve to one entry, so the step links both.
        var pack = Pack(Recipe("Garlic Rice",
            [
                Row("Onion"),
                Row("olive oil", 1, "tbsp"),
                Row("garlic", 2, "pcs"),
                Row("extra virgin olive oil", 1, "tsp", notes: "to finish"),
                Row("rice", 200, "g"),
            ],
            [
                Step("Soften the onion.", "onion"),
                Step("Fry the garlic in the oil.", "evoo", "garlic"),
                Step("Add the rice."),
            ]));

        (await ImportAsync(pack)).Succeeded.Should().BeTrue();

        var recipe = (await LoadRecipesAsync()).Single();
        var rowIdByOrder = recipe.Ingredients.ToDictionary(row => row.DisplayOrder, row => row.Id);
        string[] LinkedNames(int stepNumber) => recipe.Steps.Single(step => step.StepNumber == stepNumber)
            .StepIngredients.Select(link => recipe.Ingredients.Single(row => row.Id == link.RecipeIngredientId))
            .OrderBy(row => row.DisplayOrder)
            .Select(row => $"{row.DisplayOrder}:{row.Ingredient.Name}")
            .ToArray();

        recipe.Steps.Select(step => step.StepNumber).Should().BeEquivalentTo([1, 2, 3]);
        LinkedNames(1).Should().Equal("0:onion");
        LinkedNames(2).Should().Equal("1:olive oil", "2:garlic", "3:olive oil");
        LinkedNames(3).Should().BeEmpty();
        rowIdByOrder.Should().HaveCount(5);
    }

    // ── Faults ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task APackWithSeveralFaultsWritesNothingAndReportsEveryFault()
    {
        await WriteImageAsync(PackDirectory, "fine.jpg");

        var pack = Pack(
            Recipe("Good",          [Row("onion")], image: "fine.jpg", sourceUrl: "https://example.com/good"),
            Recipe("Unknown",       [Row("dragon fruit")]),
            Recipe("Bad Unit",      [Row("rice", 2, "handfuls")]),
            Recipe("No Servings",   [Row("rice", 200, "g")], servings: 0),
            Recipe("Missing Image", [Row("garlic")], image: "nope.jpg"),
            Recipe("Repeat One",    [Row("salt", null, null)], sourceUrl: "https://example.com/same"),
            Recipe("Repeat Two",    [Row("salt", null, null)], sourceUrl: "https://example.com/same"),
            Recipe("Bad Step",      [Row("onion")], [Step("Add the rice.", "rice")]));

        var result = await ImportAsync(pack);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().HaveCount(6);
        result.Errors.Should().ContainSingle(error => error.Contains("'Unknown'") && error.Contains("dragon fruit"));
        result.Errors.Should().ContainSingle(error => error.Contains("'Bad Unit'") && error.Contains("Unit"));
        result.Errors.Should().ContainSingle(error => error.Contains("'No Servings'") && error.Contains("Servings"));
        result.Errors.Should().ContainSingle(error => error.Contains("'Missing Image'") && error.Contains("nope.jpg"));
        result.Errors.Should().ContainSingle(error => error.Contains("https://example.com/same")
                                                      && error.Contains("Repeat One") && error.Contains("Repeat Two"));
        result.Errors.Should().ContainSingle(error => error.Contains("'Bad Step'") && error.Contains("'rice'"));

        (await RecipeCountAsync()).Should().Be(0);
        StoredImages().Should().BeEmpty();
    }

    [Fact]
    public async Task ADisallowedImageTypeIsAFault()
    {
        await WriteImageAsync(PackDirectory, "photo.gif");

        var result = await ImportAsync(Pack(Recipe("Gif", [Row("onion")], image: "photo.gif")));

        result.Errors.Should().ContainSingle().Which.Should().Contain("unsupported image type '.gif'");
    }

    [Fact]
    public async Task ZeroIsNotAnAmount()
    {
        var result = await ImportAsync(Pack(Recipe("Zero", [Row("rice", 0, "g")])));

        result.Errors.Should().ContainSingle().Which.Should().Contain("Amount");
    }

    // ── Unknown ingredients ───────────────────────────────────────────────────

    [Fact]
    public async Task AnUnknownIngredientIsRejectedByDefault()
    {
        var result = await ImportAsync(Pack(
            Recipe("Exotic", [Row("Dragon Fruit"), Row("onion"), Row("Yuzu")])));

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain(error => error.Contains("Dragon Fruit"));
        result.Errors.Should().Contain(error => error.Contains("Yuzu"));

        await using var context = db.CreateDbContext();
        (await context.Ingredients.CountAsync()).Should().Be(5);
    }

    [Fact]
    public async Task CreateMissingAddsTheIngredientToOtherWithNoDefaultUnit()
    {
        var pack = Pack(
            Recipe("Smoothie", [Row("Dragon Fruit"), Row("onion")], [Step("Blend.", "dragon fruit")]),
            Recipe("Salad",    [Row("dragon fruit", 2, "pcs")]));

        var result = await ImportAsync(pack, new RecipePackImportSettings(CreateMissingIngredients: true));

        result.Succeeded.Should().BeTrue();
        result.CreatedIngredients.Should().Equal("dragon fruit");

        await using var context = db.CreateDbContext();
        var created = await context.Ingredients.AsNoTracking().SingleAsync(row => row.Name == "dragon fruit");
        created.DisplayName.Should().Be("Dragon Fruit");
        created.Category.Should().Be(IngredientCategory.Other);
        created.DefaultUnit.Should().BeNull();

        var recipes = await LoadRecipesAsync();
        recipes.Should().OnlyContain(recipe => recipe.Ingredients.Any(row => row.IngredientId == created.Id));

        var smoothie = recipes.Single(recipe => recipe.Name == "Smoothie");
        smoothie.Steps.Single().StepIngredients.Should().ContainSingle()
            .Which.RecipeIngredientId.Should().Be(smoothie.Ingredients.Single(row => row.IngredientId == created.Id).Id);
    }

    // ── Duplicates ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ARecipeWithAKnownSourceUrlIsSkippedOnReimport()
    {
        var pack = Pack(Simple("Alpha", "https://example.com/alpha"), Simple("Beta", "https://example.com/beta"));
        await ImportAsync(pack);

        var again = await ImportAsync(pack);

        again.Succeeded.Should().BeTrue();
        again.Skipped.Should().HaveCount(2);
        again.Imported.Should().BeEmpty();
        (await RecipeCountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ForceReplacesARecipeWithAKnownSourceUrl()
    {
        await ImportAsync(Pack(Simple("Alpha", "https://example.com/alpha")));
        var originalId = (await LoadRecipesAsync()).Single().Id;

        var replacement = Pack(Recipe("Alpha Revised", [Row("garlic", 3, "pcs")], sourceUrl: "https://example.com/alpha"));
        var result = await ImportAsync(replacement, new RecipePackImportSettings(ReplaceExisting: true));

        result.Replaced.Should().ContainSingle();
        result.Imported.Should().BeEmpty();

        var recipe = (await LoadRecipesAsync()).Single();
        recipe.Id.Should().NotBe(originalId);
        recipe.Name.Should().Be("Alpha Revised");
        recipe.Ingredients.Single().Ingredient.Name.Should().Be("garlic");
    }

    [Fact]
    public async Task ARecipeAMealPlanUsesIsKeptAndTheRestOfThePackImports()
    {
        await ImportAsync(Pack(Simple("Planned", "https://example.com/planned")));

        await using (var context = db.CreateDbContext())
        {
            var recipe = await context.Recipes.SingleAsync();
            var plan   = TestDataBuilder.MealPlan();
            context.MealPlans.Add(plan);
            context.MealPlanRecipes.Add(TestDataBuilder.MealPlanRecipe(plan.Id, recipe.Id));
            await context.SaveChangesAsync();
        }

        var result = await ImportAsync(
            Pack(Simple("Planned Revised", "https://example.com/planned"), Simple("Fresh", "https://example.com/fresh")),
            new RecipePackImportSettings(ReplaceExisting: true));

        result.Succeeded.Should().BeTrue();
        result.InUse.Should().ContainSingle().Which.Should().Contain("Planned Revised");
        result.Imported.Should().ContainSingle().Which.Should().Contain("Fresh");
        (await LoadRecipesAsync()).Select(recipe => recipe.Name).Should().Equal("Fresh", "Planned");
    }

    [Fact]
    public async Task ARecipeWithoutASourceUrlDuplicatesOnReimport()
    {
        var pack = Pack(Simple("Alpha"));

        await ImportAsync(pack);
        var again = await ImportAsync(pack);

        again.Imported.Should().ContainSingle();
        (await RecipeCountAsync()).Should().Be(2);
    }

    // ── Images ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImagesAreCopiedIntoStorageAndSetOnTheRecipe()
    {
        await WriteImageAsync(Path.Combine(PackDirectory, "photos"), "alpha.png");

        var result = await ImportAsync(Pack(Recipe("Alpha", [Row("onion")], image: "photos/alpha.png")));

        result.Succeeded.Should().BeTrue();
        var recipe = (await LoadRecipesAsync()).Single();
        var stored = StoredImages().Should().ContainSingle().Subject;

        Path.GetFileName(stored).Should().Be($"{RecipePackImporter.ImageFileNamePrefix}{recipe.Id:N}.png");
        recipe.ImageUrl.Should().Be("/uploads/images/" + Path.GetFileName(stored));
        File.Exists(Path.Combine(PackDirectory, "photos", "alpha.png")).Should().BeTrue("the source is copied, not moved");
    }

    [Fact]
    public async Task APackThatFailsAfterCopyingAnImageLeavesNoFileBehind()
    {
        // Validation cannot fail after a write starts, so the database is made to: a trigger rejects
        // the second recipe after the first has been saved and its image copied.
        const string poisonedName = "Poisoned";
        await using (var context = db.CreateDbContext())
        {
            await context.Database.ExecuteSqlRawAsync($"""
                CREATE OR REPLACE FUNCTION reject_poisoned_recipe() RETURNS trigger AS $$
                BEGIN
                    IF NEW."Name" = '{poisonedName}' THEN RAISE EXCEPTION 'poisoned recipe'; END IF;
                    RETURN NEW;
                END $$ LANGUAGE plpgsql;
                CREATE TRIGGER reject_poisoned_recipe BEFORE INSERT ON "Recipes"
                    FOR EACH ROW EXECUTE FUNCTION reject_poisoned_recipe();
                """);
        }

        try
        {
            await WriteImageAsync(PackDirectory, "first.jpg");
            var pack = Pack(Recipe("First", [Row("onion")], image: "first.jpg"), Simple(poisonedName));

            var import = () => ImportAsync(pack);

            await import.Should().ThrowAsync<DbUpdateException>();
            (await RecipeCountAsync()).Should().Be(0);
            StoredImages().Should().BeEmpty();
        }
        finally
        {
            await using var context = db.CreateDbContext();
            await context.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER IF EXISTS reject_poisoned_recipe ON "Recipes";
                DROP FUNCTION IF EXISTS reject_poisoned_recipe();
                """);
        }
    }
}
