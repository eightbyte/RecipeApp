using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;
using RecipeApp.API.Services;
using RecipeApp.API.Services.Seeding;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Seeding;

/// <summary>
/// Stage 5 end to end (Phase 9 §17.3): normalised artefacts on disk → recipes in a Testcontainers
/// Postgres, through the app's own DI graph so <c>ConfirmAsync</c>, the resolver and image storage
/// are the real ones. No GPU and no network — Stage 5 makes no model call, and the deferred client
/// means the model is never even loaded.
///
/// <para>The cache and image storage are redirected to a temp directory, so nothing here touches the
/// developer's real harvest or <c>uploads/</c>.</para>
/// </summary>
[Collection("Database")]
public class SeedPersistTests(DatabaseFixture db) : IAsyncLifetime
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "recipeapp-seed-persist-tests", Guid.NewGuid().ToString("N"));

    private WebApplicationFactory<Program> _factory = null!;

    private string ImagesRoot => Path.Combine(_root, "uploads");

    public async ValueTask InitializeAsync()
    {
        await db.TruncateAllAsync();

        _factory = db.Factory.WithWebHostBuilder(builder => builder
            .UseSetting("RecipeSeeding:CacheDirectory", Path.Combine(_root, "cache"))
            .UseSetting("ImageStorage:BasePath", ImagesRoot));
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private SeedCacheStore Cache => _factory.Services.GetRequiredService<SeedCacheStore>();

    private IRecipeLibrarySeeder Seeder => _factory.Services.GetRequiredService<IRecipeLibrarySeeder>();

    // ── Fixtures ──────────────────────────────────────────────────────────────

    private async Task<Ingredient> CatalogueEntryAsync(string name, decimal? density = null, params string[] aliases)
    {
        await using var context = db.CreateDbContext();
        var ingredient = new Ingredient
        {
            Id                 = Guid.NewGuid(),
            Name               = name,
            DisplayName        = name,
            Category           = IngredientCategory.Produce,
            Aliases            = [.. aliases],
            GramsPerMillilitre = density,
        };
        context.Ingredients.Add(ingredient);
        await context.SaveChangesAsync();
        return ingredient;
    }

    /// <param name="Line">The published line — parsed/ holds it; normalised/ carries it as SourceText.</param>
    private record Line(string Text, string Name, decimal? Amount, string? Unit, decimal? SourceAmount = null, string? SourceUnit = null);

    /// <summary>
    /// Writes a matching parsed/normalised pair the way Stages 3 and 4 would, fingerprint included.
    /// </summary>
    private async Task<NormalisedSeedRecipe> GivenNormalisedAsync(
        string slug, Line[] lines, int[][]? stepIndexes = null, Func<NormalisedSeedRecipe, NormalisedSeedRecipe>? tamper = null)
    {
        var sourceUrl = "https://www.myplate.gov/recipes/" + slug;
        stepIndexes ??= [[.. Enumerable.Range(0, lines.Length)]];

        var parsed = new ParsedSeedRecipe
        {
            Slug         = slug,
            SourceUrl    = sourceUrl,
            Template     = MyPlateTemplate.Primary,
            Name         = "Recipe " + slug,
            Description  = "A description.",
            Servings     = 4,
            Notes        = "Store leftovers covered. Learn more about: Rice Grains",
            SourceCredit = "Adapted from a cookbook.",
            Ingredients  = [.. lines.Select(line => new ParsedIngredientLine(line.Text, null))],
            Steps        = [.. stepIndexes.Select((_, i) => $"Step {i + 1}.")],
        };

        await Cache.WriteParsedAsync(slug, parsed);
        var fingerprint = (await Cache.TryComputeParsedFingerprintAsync(slug))!;

        var normalised = new NormalisedSeedRecipe
        {
            Slug              = slug,
            SourceUrl         = sourceUrl,
            ParsedFingerprint = fingerprint,
            NormalisedAt      = DateTime.UtcNow,
            Attempts          = 1,
            Name              = parsed.Name,
            Description       = parsed.Description,
            Servings          = parsed.Servings,
            Notes             = parsed.Notes,
            SourceCredit      = parsed.SourceCredit,
            Ingredients       = [.. lines.Select(line => new NormalisedSeedIngredient(
                line.Name, line.Name, line.Amount, line.Unit,
                line.SourceAmount ?? line.Amount, line.SourceUnit ?? line.Unit, null, line.Text))],
            Steps             = [.. stepIndexes.Select((indexes, i) => new NormalisedSeedStep(i + 1, $"Step {i + 1}.", indexes))],
        };

        normalised = tamper?.Invoke(normalised) ?? normalised;
        await Cache.WriteNormalisedAsync(slug, normalised);
        return normalised;
    }

    private static SeedManifest ManifestOf(params string[] slugs) => new()
    {
        Recipes = [.. slugs.Select(slug => new SeedManifestEntry(
            slug, "20250101000000", "https://www.myplate.gov/recipes/" + slug))],
    };

    private async Task<Recipe> LoadRecipeAsync(string slug)
    {
        await using var context = db.CreateDbContext();
        return await context.Recipes
            .Include(recipe => recipe.Ingredients).ThenInclude(row => row.Ingredient)
            .Include(recipe => recipe.Steps).ThenInclude(step => step.StepIngredients)
            .AsNoTracking()
            .SingleAsync(recipe => recipe.SourceUrl == "https://www.myplate.gov/recipes/" + slug);
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    [Fact]
    public async Task PersistsIngredientsStepsLinksAndProvenance()
    {
        var rice  = await CatalogueEntryAsync("rice");
        var onion = await CatalogueEntryAsync("onion");
        await GivenNormalisedAsync("rice-pilaf",
            [new Line("2 cups rice", "rice", 2m, "cup"), new Line("1 onion, diced", "onion", 1m, "pcs")],
            [[1], [0, 1]]);

        var result = await Seeder.PersistAsync(ManifestOf("rice-pilaf"));

        result.Persisted.Should().Be(1);
        result.Failures.Should().BeEmpty();

        var recipe = await LoadRecipeAsync("rice-pilaf");
        recipe.SourceUrl.Should().StartWith("https://www.myplate.gov/").And.NotContain("web.archive.org");
        recipe.LastCookedAt.Should().BeNull();
        recipe.Servings.Should().Be(4);

        var rows = recipe.Ingredients.OrderBy(row => row.DisplayOrder).ToList();
        rows.Select(row => row.IngredientId).Should().Equal(rice.Id, onion.Id);
        rows[0].SourceAmount.Should().Be(2m);
        rows[0].SourceUnit.Should().Be("cup");

        var steps = recipe.Steps.OrderBy(step => step.StepNumber).ToList();
        steps.Select(step => step.StepNumber).Should().Equal(1, 2);
        steps[0].StepIngredients.Select(link => link.RecipeIngredientId).Should().Equal(rows[1].Id);
        steps[1].StepIngredients.Select(link => link.RecipeIngredientId)
            .Should().BeEquivalentTo([rows[0].Id, rows[1].Id]);
    }

    [Fact]
    public async Task ComposesTheDescriptionFromDescriptionNotesCreditAndAttribution()
    {
        await CatalogueEntryAsync("rice");
        await GivenNormalisedAsync("plain-rice", [new Line("1 cup rice", "rice", 1m, "cup")]);

        await Seeder.PersistAsync(ManifestOf("plain-rice"));

        var attribution = _factory.Services.GetRequiredService<IOptions<RecipeSeedingOptions>>().Value.SeedAttributionNote;
        (await LoadRecipeAsync("plain-rice")).Description.Should().Be(
            $"A description.\n\nStore leftovers covered.\n\nAdapted from a cookbook.\n\n{attribution}");
    }

    [Fact]
    public async Task ResolvesCupsThroughDensityOnlyWhereTheEntryHasOne()
    {
        const decimal flourDensity = 0.53m;
        await CatalogueEntryAsync("flour", flourDensity);
        await CatalogueEntryAsync("spinach");
        await GivenNormalisedAsync("flour-and-spinach",
            [new Line("2 cups flour", "flour", 2m, "cup"), new Line("2 cups spinach", "spinach", 2m, "cup")]);

        await Seeder.PersistAsync(ManifestOf("flour-and-spinach"));

        var expected = new MeasurementConverter(Options.Create(new MeasurementOptions()))
            .ResolveForImport(2m, "cup", flourDensity);

        var rows = (await LoadRecipeAsync("flour-and-spinach")).Ingredients.OrderBy(row => row.DisplayOrder).ToList();
        rows[0].Unit.Should().Be(expected.Unit).And.Be("g");
        rows[0].Amount.Should().BeApproximately(expected.Amount, 0.01m);
        rows[1].Unit.Should().Be("cup");
        rows[1].Amount.Should().Be(2m);

        // Provenance is the source measurement either way — never the converted one.
        rows[0].SourceAmount.Should().Be(2m);
        rows[0].SourceUnit.Should().Be("cup");
    }

    [Fact]
    public async Task ReusesCatalogueEntriesInsteadOfCreatingThem()
    {
        await CatalogueEntryAsync("tomato", null, "tomatoes");
        await GivenNormalisedAsync("tomato-salad", [new Line("2 tomatoes", "tomatoes", 2m, "pcs")]);

        await Seeder.PersistAsync(ManifestOf("tomato-salad"));

        await using var context = db.CreateDbContext();
        (await context.Ingredients.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CopiesTheHarvestedPhotoAndSetsImageUrl()
    {
        await CatalogueEntryAsync("rice");
        await GivenNormalisedAsync("photo-rice", [new Line("1 cup rice", "rice", 1m, "cup")]);
        await Cache.WriteImageAsync("photo-rice", [0xFF, 0xD8, 0xFF, 0xE0], ".jpg");

        var result = await Seeder.PersistAsync(ManifestOf("photo-rice"));

        result.WithoutImage.Should().Be(0);
        var recipe = await LoadRecipeAsync("photo-rice");
        recipe.ImageUrl.Should().Be(ImageService.PublicPathPrefix + "myplate-photo-rice.jpg");
        File.Exists(Path.Combine(ImagesRoot, "myplate-photo-rice.jpg")).Should().BeTrue();
    }

    [Fact]
    public async Task ARecipeWithNoPhotoKeepsANullImageUrl()
    {
        await CatalogueEntryAsync("rice");
        await GivenNormalisedAsync("no-photo", [new Line("1 cup rice", "rice", 1m, "cup")]);

        var result = await Seeder.PersistAsync(ManifestOf("no-photo"));

        result.WithoutImage.Should().Be(1);
        (await LoadRecipeAsync("no-photo")).ImageUrl.Should().BeNull();
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RunningTwiceYieldsOneRecipe()
    {
        await CatalogueEntryAsync("rice");
        await GivenNormalisedAsync("twice", [new Line("1 cup rice", "rice", 1m, "cup")]);

        await Seeder.PersistAsync(ManifestOf("twice"));
        var second = await Seeder.PersistAsync(ManifestOf("twice"));

        second.Persisted.Should().Be(0);
        second.AlreadyPersisted.Should().Be(1);

        await using var context = db.CreateDbContext();
        (await context.Recipes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ForceReplacesRatherThanDuplicating()
    {
        await CatalogueEntryAsync("rice");
        await GivenNormalisedAsync("forced", [new Line("1 cup rice", "rice", 1m, "cup")]);
        await Seeder.PersistAsync(ManifestOf("forced"));
        var firstId = (await LoadRecipeAsync("forced")).Id;

        var result = await Seeder.PersistAsync(ManifestOf("forced"), force: true);

        result.Persisted.Should().Be(1);
        result.Replaced.Should().Be(1);

        await using var context = db.CreateDbContext();
        (await context.Recipes.CountAsync()).Should().Be(1);
        (await LoadRecipeAsync("forced")).Id.Should().NotBe(firstId);
    }

    [Fact]
    public async Task ForceLeavesARecipeInAMealPlanInPlace()
    {
        await CatalogueEntryAsync("rice");
        await GivenNormalisedAsync("planned", [new Line("1 cup rice", "rice", 1m, "cup")]);
        await Seeder.PersistAsync(ManifestOf("planned"));
        var recipeId = (await LoadRecipeAsync("planned")).Id;

        await using (var context = db.CreateDbContext())
        {
            var plan = new MealPlan { Id = Guid.NewGuid(), Name = "This week", IsActive = true };
            context.MealPlans.Add(plan);
            context.MealPlanRecipes.Add(new MealPlanRecipe { Id = Guid.NewGuid(), MealPlanId = plan.Id, RecipeId = recipeId });
            await context.SaveChangesAsync();
        }

        var result = await Seeder.PersistAsync(ManifestOf("planned"), force: true);

        result.Persisted.Should().Be(0);
        result.Failures["planned"].Should().Contain(nameof(SeedPersistFailure.RecipeInUse));
        (await LoadRecipeAsync("planned")).Id.Should().Be(recipeId);
    }

    // ── Preparation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SkipsListedHeadingRowsAndKeepsTheLinksPointingAtTheRightRows()
    {
        // citrus-salad lists "Dressing" in SeedNonIngredientRows.
        await CatalogueEntryAsync("orange");
        await CatalogueEntryAsync("vinegar");
        await CatalogueEntryAsync("dressing");   // a heading that would resolve is the dangerous case
        await GivenNormalisedAsync("citrus-salad",
            [new Line("1 orange", "orange", 1m, "pcs"), new Line("Dressing", "dressing", null, null),
             new Line("2 tablespoons vinegar", "vinegar", 2m, "tbsp")],
            [[0], [1, 2]]);

        var result = await Seeder.PersistAsync(ManifestOf("citrus-salad"));

        result.NonIngredientRowsRemoved.Should().Be(1);
        var recipe = await LoadRecipeAsync("citrus-salad");
        var rows   = recipe.Ingredients.OrderBy(row => row.DisplayOrder).ToList();
        rows.Select(row => row.Ingredient.Name).Should().Equal("orange", "vinegar");

        var secondStep = recipe.Steps.Single(step => step.StepNumber == 2);
        secondStep.StepIngredients.Select(link => link.RecipeIngredientId).Should().Equal(rows[1].Id);
    }

    [Fact]
    public async Task StoresAnUnexplainedAmountUnquantifiedWithTheLineInItsNotes()
    {
        await CatalogueEntryAsync("vinaigrette dressing");
        const string line = "1/3 cup vinaigrette dressing (around 15 calories per tablespoon)";
        await GivenNormalisedAsync("sunny-salad", [new Line(line, "vinaigrette dressing", 13.333m, "cup")]);

        var result = await Seeder.PersistAsync(ManifestOf("sunny-salad"));

        result.AmountsDropped.Should().Be(1);
        var row = (await LoadRecipeAsync("sunny-salad")).Ingredients.Single();
        row.Amount.Should().BeNull();
        row.Unit.Should().BeNull();
        row.SourceAmount.Should().BeNull();
        row.SourceUnit.Should().BeNull();
        row.Notes.Should().Contain(line);
    }

    [Fact]
    public async Task KeepsACountTimesPackSizeAmount()
    {
        await CatalogueEntryAsync("kidney beans");
        await GivenNormalisedAsync("bean-pot",
            [new Line("2 cans (15.5 ounces each) kidney beans", "kidney beans", 878.8m, "g", 31m, "ounces")]);

        var result = await Seeder.PersistAsync(ManifestOf("bean-pot"));

        result.AmountsDropped.Should().Be(0);
        (await LoadRecipeAsync("bean-pot")).Ingredients.Single().Amount.Should().Be(878.8m);
    }

    // ── Rejections ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ACatalogueMissFailsThatRecipeAndTheRunContinues()
    {
        await CatalogueEntryAsync("rice");
        await GivenNormalisedAsync("a-miss", [new Line("1 kohlrabi", "kohlrabi", 1m, "pcs")]);
        await GivenNormalisedAsync("a-hit", [new Line("1 cup rice", "rice", 1m, "cup")]);

        var result = await Seeder.PersistAsync(ManifestOf("a-miss", "a-hit"));

        result.Persisted.Should().Be(1);
        result.Failures["a-miss"].Should().Contain(nameof(SeedPersistFailure.CatalogueMiss)).And.Contain("kohlrabi");
    }

    [Fact]
    public async Task AnArtefactWithADifferentRowCountThanItsParsedPageIsRejected()
    {
        // §24.4.2: a "correct" hand edit to normalised/ alone still breaks the pairing with parsed/.
        await CatalogueEntryAsync("rice");
        await GivenNormalisedAsync("edited", [new Line("1 cup rice", "rice", 1m, "cup"), new Line("Heading", "rice", null, null)],
            [[0]],
            tamper: recipe => recipe with { Ingredients = [recipe.Ingredients[0]] });

        var result = await Seeder.PersistAsync(ManifestOf("edited"));

        result.Failures["edited"].Should().Contain(nameof(SeedPersistFailure.IngredientCountMismatch));
    }

    [Fact]
    public async Task AStepPointingPastTheIngredientsIsRejected()
    {
        await CatalogueEntryAsync("rice");
        await GivenNormalisedAsync("overrun", [new Line("1 cup rice", "rice", 1m, "cup")], [[0, 1]]);

        var result = await Seeder.PersistAsync(ManifestOf("overrun"));

        result.Failures["overrun"].Should().Contain(nameof(SeedPersistFailure.IngredientIndexOutOfRange));
    }

    [Fact]
    public async Task AnArtefactDerivedFromAnOlderParseIsRejected()
    {
        await CatalogueEntryAsync("rice");
        await GivenNormalisedAsync("reparsed", [new Line("1 cup rice", "rice", 1m, "cup")],
            tamper: recipe => recipe with { ParsedFingerprint = "an-older-parse" });

        var result = await Seeder.PersistAsync(ManifestOf("reparsed"));

        result.Failures["reparsed"].Should().Contain(nameof(SeedPersistFailure.StaleArtefact));
    }

    [Fact]
    public async Task AnEmptyCatalogueStopsTheRunBeforeAnyRecipe()
    {
        await GivenNormalisedAsync("no-catalogue", [new Line("1 cup rice", "rice", 1m, "cup")]);

        var run = () => Seeder.PersistAsync(ManifestOf("no-catalogue"));

        (await run.Should().ThrowAsync<SeedHarvestException>()).Which.Message.Should().Contain("import-catalogue");
    }

    [Fact]
    public async Task ARecipeWithNoNormalisedArtefactIsCountedNotFailed()
    {
        await CatalogueEntryAsync("rice");

        var result = await Seeder.PersistAsync(ManifestOf("never-normalised"));

        result.NotNormalised.Should().Be(1);
        result.Failures.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordsPersistedInState()
    {
        await CatalogueEntryAsync("rice");
        await GivenNormalisedAsync("stateful", [new Line("1 cup rice", "rice", 1m, "cup")]);

        await Seeder.PersistAsync(ManifestOf("stateful"));

        (await Cache.LoadStateAsync()).Slugs["stateful"].Stage.Should().Be(SeedStage.Persisted);
    }
}
