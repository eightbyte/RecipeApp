using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;
using RecipeApp.API.Services;
using RecipeApp.API.Services.Seeding;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Seeding;

/// <summary>
/// Stage 5's ingredient resolution (Phase 9.3 §4.8): a dictionary lookup where Phase 9 §12 had a
/// per-recipe LLM call whose candidate list grew as the run proceeded.
///
/// <para>The resolver takes no <c>ILlmStructuredClient</c> at all, which is the strongest form the
/// "zero LLM calls" guarantee can take — there is no seam through which inference could happen.
/// These tests exercise the lookup, the name-beats-alias rule, and the density arm of Stage B.</para>
/// </summary>
[Collection("Database")]
public class SeedCatalogueResolverTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private SeedCatalogueResolver BuildResolver() => new(
        db.CreateDbContext(),
        new MeasurementConverter(Options.Create(new MeasurementOptions())),
        NullLogger<SeedCatalogueResolver>.Instance);

    private async Task<Ingredient> SeedIngredientAsync(
        string name, string[]? aliases = null, decimal? density = null)
    {
        await using var context = db.CreateDbContext();

        var ingredient = new Ingredient
        {
            Id                 = Guid.NewGuid(),
            Name               = name,
            DisplayName        = name,
            Category           = IngredientCategory.Produce,
            Aliases            = aliases ?? [],
            GramsPerMillilitre = density,
        };

        context.Ingredients.Add(ingredient);
        await context.SaveChangesAsync();
        return ingredient;
    }

    private static NormalisedSeedRecipe Recipe(
        params (string Name, decimal? Amount, string? Unit)[] ingredients) => new()
    {
        Slug              = "test-recipe",
        SourceUrl         = "https://www.myplate.gov/recipes/test-recipe",
        ParsedFingerprint = "deadbeef",
        NormalisedAt      = DateTime.UtcNow,
        Attempts          = 1,
        Name              = "Test Recipe",
        Servings          = 4,
        Ingredients       = [.. ingredients.Select(i => new NormalisedSeedIngredient(
            i.Name, i.Name, i.Amount, i.Unit, i.Amount, i.Unit, "chopped", $"{i.Amount} {i.Unit} {i.Name}"))],
        Steps             = [new NormalisedSeedStep(1, "Cook it.", [0])],
    };

    // ── Resolution ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolvesOnAName()
    {
        var chickpeas = await SeedIngredientAsync("chickpeas");
        var resolver  = BuildResolver();

        var request = resolver.ToConfirmRequest(
            Recipe(("chickpeas", 1m, "cup")), await resolver.LoadAsync());

        request.Ingredients.Single().IngredientId.Should().Be(chickpeas.Id);
        request.Ingredients.Single().NewIngredientName.Should().BeNull();
    }

    [Fact]
    public async Task ResolvesOnAnAlias()
    {
        // The whole reason Aliases is a column rather than a detail of the seed artefact.
        var chickpeas = await SeedIngredientAsync("chickpeas", ["garbanzo beans"]);
        var resolver  = BuildResolver();

        var request = resolver.ToConfirmRequest(
            Recipe(("garbanzo beans", 1m, "cup")), await resolver.LoadAsync());

        request.Ingredients.Single().IngredientId.Should().Be(chickpeas.Id);
    }

    [Fact]
    public async Task ANameBeatsAnotherEntrysAlias()
    {
        // The artefact's validation forbids this collision outright. This is what makes the outcome
        // defined anyway if a hand-edited row ever introduces one.
        await SeedIngredientAsync("tomato", ["diced tomatoes"]);
        var diced = await SeedIngredientAsync("diced tomatoes");

        var resolver = BuildResolver();
        var request  = resolver.ToConfirmRequest(
            Recipe(("diced tomatoes", 400m, "g")), await resolver.LoadAsync());

        request.Ingredients.Single().IngredientId.Should().Be(diced.Id);
    }

    [Fact]
    public async Task AMissFailsTheRecipeAndNamesEveryUnresolvedIngredient()
    {
        await SeedIngredientAsync("onion");
        var resolver = BuildResolver();
        var lookup   = await resolver.LoadAsync();

        var resolve = () => resolver.ToConfirmRequest(
            Recipe(("onion", 1m, "pcs"), ("kohlrabi", 1m, "pcs"), ("salsify", 1m, "pcs")), lookup);

        var thrown = resolve.Should().Throw<SeedCatalogueResolutionException>().Which;

        thrown.Slug.Should().Be("test-recipe");
        thrown.UnresolvedNames.Should().BeEquivalentTo(["kohlrabi", "salsify"]);
        thrown.Message.Should().Contain("--build-catalogue");
    }

    [Fact]
    public async Task AllowUnknownIngredientsFallsBackToCreatingTheRow()
    {
        await SeedIngredientAsync("onion");
        var resolver = BuildResolver();

        var request = resolver.ToConfirmRequest(
            Recipe(("kohlrabi", 1m, "pcs")), await resolver.LoadAsync(),
            allowUnknownIngredients: true);

        var row = request.Ingredients.Single();
        row.IngredientId.Should().BeNull();
        row.NewIngredientName.Should().Be("kohlrabi");
        row.Category.Should().Be(IngredientCategory.Other);
    }

    // ── Stage B: measurement ──────────────────────────────────────────────────

    [Fact]
    public async Task ResolvesACupAgainstTheMatchedEntrysDensity()
    {
        // 120 g per cup — King Arthur's all-purpose flour figure, as IngredientDensitySeeder holds it.
        await SeedIngredientAsync("all-purpose flour", density: 0.5m);
        var resolver = BuildResolver();

        var request = resolver.ToConfirmRequest(
            Recipe(("all-purpose flour", 2m, MeasurementUnit.Cup)), await resolver.LoadAsync());

        var row = request.Ingredients.Single();
        row.Unit.Should().Be(MeasurementUnit.Gram);
        row.Amount.Should().Be(240m);

        // Provenance survives the conversion — never summed, never consolidated, but always audit-able.
        row.SourceAmount.Should().Be(2m);
        row.SourceUnit.Should().Be(MeasurementUnit.Cup);
    }

    [Fact]
    public async Task KeepsACupAsStatedWhenTheEntryHasNoDensity()
    {
        // GramsPerMillilitre == null means "no reliable density — do not invent a mass". Liquids
        // and packing-dominated ingredients are deliberately left null.
        await SeedIngredientAsync("water");
        var resolver = BuildResolver();

        var request = resolver.ToConfirmRequest(
            Recipe(("water", 2m, MeasurementUnit.Cup)), await resolver.LoadAsync());

        var row = request.Ingredients.Single();
        row.Unit.Should().Be(MeasurementUnit.Cup);
        row.Amount.Should().Be(2m);
    }

    [Fact]
    public async Task AnUnquantifiedRowStaysUnquantified()
    {
        // Phase 9.1: null means "the source states no quantity". Never zero — 0 renders as
        // '0 g Salt' and sums into shopping lists.
        await SeedIngredientAsync("salt", density: 1.2m);
        var resolver = BuildResolver();

        var request = resolver.ToConfirmRequest(
            Recipe(("salt", null, null)), await resolver.LoadAsync());

        var row = request.Ingredients.Single();
        row.Amount.Should().BeNull();
        row.Unit.Should().BeNull();
    }

    // ── The request as a whole ────────────────────────────────────────────────

    [Fact]
    public async Task CarriesTheRecipesOwnMetadataAndStepLinkage()
    {
        await SeedIngredientAsync("onion");
        var resolver = BuildResolver();

        var request = resolver.ToConfirmRequest(
            Recipe(("onion", 1m, "pcs")), await resolver.LoadAsync());

        request.Name.Should().Be("Test Recipe");
        request.Servings.Should().Be(4);
        request.SourceUrl.Should().Be("https://www.myplate.gov/recipes/test-recipe");
        request.Ingredients.Single().Notes.Should().Be("chopped");
        request.Ingredients.Single().DisplayOrder.Should().Be(0);
        request.Steps.Single().IngredientIndexes.Should().Equal(0);
    }

    [Fact]
    public async Task DisplayOrderFollowsTheNormalisedOrderExactly()
    {
        // Step linkage is by position into the ingredients array, so a reordering here would
        // silently relink every step in the corpus.
        await SeedIngredientAsync("onion");
        await SeedIngredientAsync("garlic");
        await SeedIngredientAsync("carrot");

        var resolver = BuildResolver();
        var request  = resolver.ToConfirmRequest(
            Recipe(("onion", 1m, "pcs"), ("garlic", 2m, "pcs"), ("carrot", 3m, "pcs")),
            await resolver.LoadAsync());

        request.Ingredients.Select(i => i.DisplayOrder).Should().Equal(0, 1, 2);
    }
}
