using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;
using RecipeApp.API.Services.Seeding;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Data;

/// <summary>
/// The two sample recipes, after Phase 9.3 stopped <see cref="DataSeeder"/> being a second source
/// of catalogue truth (§4.9).
///
/// <para>Two things here are easy to break and silent when broken: the entry guard, which now keys
/// on recipes rather than ingredients, and the alias resolution that stops <c>capsicum</c> sitting
/// permanently beside <c>bell pepper</c>.</para>
/// </summary>
[Collection("Database")]
public class DataSeederTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static IngredientCatalogueFile Catalogue(params IngredientCatalogueEntry[] entries) => new()
    {
        GeneratedAt = DateTime.UtcNow,
        Source      = new CatalogueSource("myplate", 1123, 8882, 1475),
        Entries     = entries,
    };

    [Fact]
    public async Task SeedsBothSampleRecipesIntoAnEmptyDatabase()
    {
        await using var context = db.CreateDbContext();
        await DataSeeder.SeedAsync(context);

        var recipes = await context.Recipes.Select(r => r.Name).ToListAsync();
        recipes.Should().BeEquivalentTo(["Spaghetti Bolognese", "Chicken Stir-Fry"]);
    }

    [Fact]
    public async Task StillSeedsWhenTheCatalogueHasAlreadyBeenLoaded()
    {
        // The regression the guard change exists to prevent. It used to return early if any
        // ingredient existed, which was sound while this was the only seeder — but the catalogue is
        // now loaded first by design, so that guard would fire every time and the sample recipes
        // would silently never appear.
        await using var context = db.CreateDbContext();

        await IngredientCatalogueFileSeeder.SeedAsync(context, Catalogue(
            new IngredientCatalogueEntry("onion", "Onion", IngredientCategory.Produce, "pcs", [], 1, 1)));

        await DataSeeder.SeedAsync(context);

        (await context.Recipes.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ReRunningIsANoOp()
    {
        await using var context = db.CreateDbContext();

        await DataSeeder.SeedAsync(context);
        await DataSeeder.SeedAsync(context);

        (await context.Recipes.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ResolvesSampleIngredientsThroughTheCatalogueRatherThanDuplicatingThem()
    {
        await using var context = db.CreateDbContext();

        await IngredientCatalogueFileSeeder.SeedAsync(context, Catalogue(
            new IngredientCatalogueEntry("onion", "Onion", IngredientCategory.Produce, "pcs", [], 352, 300),
            new IngredientCatalogueEntry("garlic", "Garlic", IngredientCategory.Produce, "pcs", [], 56, 50)));

        await DataSeeder.SeedAsync(context);

        (await context.Ingredients.CountAsync(i => i.Name == "onion")).Should().Be(1);
        (await context.Ingredients.CountAsync(i => i.Name == "garlic")).Should().Be(1);

        // The catalogue's row is the one the recipes point at, not a second copy beside it.
        var onion = await context.Ingredients.AsNoTracking().SingleAsync(i => i.Name == "onion");
        var bolognese = await context.Recipes
            .Include(r => r.Ingredients)
            .SingleAsync(r => r.Name == "Spaghetti Bolognese");

        bolognese.Ingredients.Should().Contain(ri => ri.IngredientId == onion.Id);
    }

    [Fact]
    public async Task RegionalSpellingsResolveToTheirAmericanEntry()
    {
        // §2.2's finding, fixed. 'capsicum', 'plain flour' and 'beef stock' named nothing in the
        // corpus; seeded as they stood they never consolidated with their American twin.
        await using var context = db.CreateDbContext();

        await IngredientCatalogueFileSeeder.SeedAsync(context, Catalogue(
            new IngredientCatalogueEntry("bell pepper", "Bell Pepper", IngredientCategory.Produce,
                "pcs", ["capsicum"], 34, 30),
            new IngredientCatalogueEntry("beef broth", "Beef Broth", IngredientCategory.Canned,
                "ml", ["beef stock"], 5, 5)));

        await DataSeeder.SeedAsync(context);

        (await context.Ingredients.AnyAsync(i => i.Name == "capsicum")).Should().BeFalse();
        (await context.Ingredients.AnyAsync(i => i.Name == "beef stock")).Should().BeFalse();
        (await context.Ingredients.CountAsync(i => i.Name == "bell pepper")).Should().Be(1);
        (await context.Ingredients.CountAsync(i => i.Name == "beef broth")).Should().Be(1);
    }

    [Fact]
    public async Task EverySampleRecipeIngredientResolvesToARow()
    {
        await using var context = db.CreateDbContext();
        await DataSeeder.SeedAsync(context);

        var orphans = await context.RecipeIngredients
            .Where(ri => !context.Ingredients.Any(i => i.Id == ri.IngredientId))
            .CountAsync();

        orphans.Should().Be(0);
        (await context.RecipeIngredients.CountAsync()).Should().Be(22);
    }

    [Fact]
    public async Task StepsLinkToTheIngredientsTheyName()
    {
        await using var context = db.CreateDbContext();
        await DataSeeder.SeedAsync(context);

        (await context.RecipeSteps.CountAsync()).Should().Be(14);
        (await context.RecipeStepIngredients.CountAsync()).Should().BeGreaterThan(0);
    }
}
