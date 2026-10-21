using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeApp.API.Data;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;
using RecipeApp.API.Services.RecipePacks;
using RecipeApp.API.Services.Seeding;
using RecipeApp.Tests.Helpers;
using RecipeApp.Tests.Infrastructure;
using static RecipeApp.Tests.RecipePacks.RecipePackFixtures;

namespace RecipeApp.Tests.Data;

/// <summary>
/// First-run seeding against a real Postgres (Phase 10 §4.2, §4.3, §6), with the shipped catalogue
/// and the shipped sample pack unless a test points the pack elsewhere.
/// </summary>
[Collection("Database")]
public class FirstRunSeederTests(DatabaseFixture db) : IAsyncLifetime
{
    private const int SampleRecipeCount = 12;

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "recipeapp-first-run-tests", Guid.NewGuid().ToString("N"));

    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public ValueTask InitializeAsync() => db.TruncateAllAsync();

    public async ValueTask DisposeAsync()
    {
        foreach (var factory in _factories) await factory.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private WebApplicationFactory<Program> Factory(string? samplePackPath = null)
    {
        var factory = samplePackPath is null
            ? db.Factory.WithWebHostBuilder(_ => { })
            : db.Factory.WithWebHostBuilder(builder =>
                builder.UseSetting("RecipePacks:SamplePackFilePath", samplePackPath));
        _factories.Add(factory);
        return factory;
    }

    private static async Task RunAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<FirstRunSeeder>().RunAsync();
    }

    private Task RunAsync() => RunAsync(Factory());

    private async Task<Dictionary<string, SeedLedgerEntry>> LedgerAsync()
    {
        await using var context = db.CreateDbContext();
        return await context.SeedLedger.AsNoTracking().ToDictionaryAsync(entry => entry.Key);
    }

    private async Task<(int Ingredients, int Recipes)> CountsAsync()
    {
        await using var context = db.CreateDbContext();
        return (await context.Ingredients.CountAsync(), await context.Recipes.CountAsync());
    }

    private async Task<IngredientCatalogueFile> ShippedCatalogueAsync()
    {
        using var scope = Factory().Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IngredientCatalogueFileStore>().LoadAsync();
    }

    private async Task ExistingIngredientAsync()
    {
        await using var context = db.CreateDbContext();
        context.Ingredients.Add(TestDataBuilder.Ingredient());
        await context.SaveChangesAsync();
    }

    // ── A fresh database ──────────────────────────────────────────────────────

    [Fact]
    public async Task AFreshDatabaseSeedsTheCatalogueAndEverySampleRecipe()
    {
        await RunAsync();

        var catalogue = await ShippedCatalogueAsync();
        var pack      = await RecipePackFileStore.LoadAsync(RepositoryPaths.ShippedSamplePackPath);

        var (ingredients, recipes) = await CountsAsync();
        ingredients.Should().Be(catalogue.Entries.Count, "every sample ingredient resolves to a catalogue entry");
        recipes.Should().Be(SampleRecipeCount);

        var ledger = await LedgerAsync();
        ledger.Keys.Should().BeEquivalentTo(SeedLedgerKeys.All);
        ledger.Values.Should().OnlyContain(entry => entry.Outcome == SeedLedgerOutcome.Applied);
        ledger[SeedLedgerKeys.IngredientCatalogue].ArtefactVersion.Should().Be(catalogue.ContentVersion);
        ledger[SeedLedgerKeys.SampleRecipes].ArtefactVersion.Should().Be(pack.ContentVersion);
    }

    [Fact]
    public async Task DensitiesAreAppliedToTheSeededCatalogue()
    {
        await RunAsync();

        await using var context = db.CreateDbContext();
        (await context.Ingredients.SingleAsync(row => row.Name == "all-purpose flour"))
            .GramsPerMillilitre.Should().NotBeNull("the pancakes' cup of flour needs a density");
    }

    [Fact]
    public async Task ASecondRunIsANoOp()
    {
        await RunAsync();
        var countsBefore = await CountsAsync();
        var ledgerBefore = await LedgerAsync();

        await RunAsync();

        (await CountsAsync()).Should().Be(countsBefore);
        (await LedgerAsync()).Values.Should().BeEquivalentTo(ledgerBefore.Values);
    }

    [Fact]
    public async Task DeletedSampleRecipesDoNotComeBack()
    {
        // D2: the ledger, not an empty Recipes table, says the step has run.
        await RunAsync();
        await using (var context = db.CreateDbContext())
            await context.Recipes.ExecuteDeleteAsync();

        await RunAsync();

        (await CountsAsync()).Recipes.Should().Be(0);
    }

    // ── A database that already has data (§4.3) ──────────────────────────────

    [Fact]
    public async Task ExistingRecipesWithNoLedgerAreLeftAloneAndRecordedAsSkipped()
    {
        await ExistingIngredientAsync();
        await using (var context = db.CreateDbContext())
        {
            context.Recipes.Add(TestDataBuilder.Recipe());
            await context.SaveChangesAsync();
        }

        await RunAsync();

        (await CountsAsync()).Should().Be((1, 1));
        (await LedgerAsync()).Values.Should().OnlyContain(entry => entry.Outcome == SeedLedgerOutcome.SkippedExistingData)
            .And.HaveCount(2);
    }

    [Fact]
    public async Task ExistingIngredientsWithNoLedgerAreLeftAloneAndRecordedAsSkipped()
    {
        await ExistingIngredientAsync();

        await RunAsync();

        (await CountsAsync()).Ingredients.Should().Be(1);
        (await LedgerAsync())[SeedLedgerKeys.IngredientCatalogue].Outcome
            .Should().Be(SeedLedgerOutcome.SkippedExistingData);
    }

    // ── Failures ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnUnresolvableSamplePackWritesNothingAndDoesNotThrow()
    {
        var brokenPack = await WriteAsync(_root, Pack(Recipe("Mystery Stew", [Row("unobtainium")])));

        var run = () => RunAsync(Factory(brokenPack));

        await run.Should().NotThrowAsync();
        (await CountsAsync()).Recipes.Should().Be(0);
        (await LedgerAsync()).Keys.Should().Equal(SeedLedgerKeys.IngredientCatalogue);
    }

    [Fact]
    public async Task AMissingSamplePackWritesNothingAndDoesNotThrow()
    {
        var run = () => RunAsync(Factory(Path.Combine(_root, "absent.json")));

        await run.Should().NotThrowAsync();
        (await LedgerAsync()).Keys.Should().Equal(SeedLedgerKeys.IngredientCatalogue);
    }

    [Fact]
    public async Task AfterARecipeStepFailureOnlyTheRecipeStepRetries()
    {
        var brokenPack = await WriteAsync(_root, Pack(Recipe("Mystery Stew", [Row("unobtainium")])));
        await RunAsync(Factory(brokenPack));
        var catalogueEntry = (await LedgerAsync())[SeedLedgerKeys.IngredientCatalogue];
        var ingredientsAfterFirstRun = (await CountsAsync()).Ingredients;

        await RunAsync();

        var ledger = await LedgerAsync();
        ledger[SeedLedgerKeys.IngredientCatalogue].Should().BeEquivalentTo(catalogueEntry, "the catalogue step did not run again");
        ledger[SeedLedgerKeys.SampleRecipes].Outcome.Should().Be(SeedLedgerOutcome.Applied);
        (await CountsAsync()).Should().Be((ingredientsAfterFirstRun, SampleRecipeCount));
    }

    [Fact]
    public async Task AStepAnotherHostAlreadyRecordedIsNotRunAgain()
    {
        // The ledger row is the only evidence consulted: a recorded step is skipped even though its
        // target is empty.
        await using (var context = db.CreateDbContext())
        {
            context.SeedLedger.Add(new SeedLedgerEntry
            {
                Key = SeedLedgerKeys.SampleRecipes, Outcome = SeedLedgerOutcome.Applied, ArtefactVersion = 1,
            });
            await context.SaveChangesAsync();
        }

        await RunAsync();

        (await CountsAsync()).Recipes.Should().Be(0);
    }

    // ── Startup ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheTestingEnvironmentSeedsNothingAtStartup()
    {
        // Building a fresh host runs Program.cs's startup path, under RecipeAppFactory's Testing environment.
        _ = Factory().Services;

        (await CountsAsync()).Should().Be((0, 0));
        (await LedgerAsync()).Should().BeEmpty();
    }
}
