using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;
using RecipeApp.API.Services.Seeding;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Data;

/// <summary>The seeding half of Stage 4.5, artefact into Postgres with no LLM or network, under the contract fill nulls and never overwrite.</summary>
[Collection("Database")]
public class IngredientCatalogueFileSeederTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IngredientCatalogueFile Catalogue(params IngredientCatalogueEntry[] entries) => new()
    {
        ContentVersion = 1,
        UpdatedAt      = DateTime.UtcNow,
        Provenance     = CatalogueProvenance.Curated,
        Entries        = entries,
    };

    private static IngredientCatalogueEntry Entry(
        string name,
        string? displayName = null,
        string category = IngredientCategory.Produce,
        string? defaultUnit = MeasurementUnit.Piece,
        string[]? aliases = null,
        bool isStaple = false) =>
        new(name, displayName ?? name, category, defaultUnit, aliases ?? [], isStaple);

    private async Task<Ingredient> SeedRowAsync(Ingredient row)
    {
        await using var context = db.CreateDbContext();
        context.Ingredients.Add(row);
        await context.SaveChangesAsync();
        return row;
    }

    private async Task<Ingredient> ReadAsync(string name)
    {
        await using var context = db.CreateDbContext();
        return await context.Ingredients.AsNoTracking().SingleAsync(i => i.Name == name);
    }

    // ── Insert ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InsertsEveryEntryIntoAnEmptyCatalogue()
    {
        await using var context = db.CreateDbContext();

        var outcome = await IngredientCatalogueFileSeeder.SeedAsync(context, Catalogue(
            Entry("onion", "Onion", IngredientCategory.Produce, MeasurementUnit.Piece, ["onions"]),
            Entry("milk", "Milk", IngredientCategory.Dairy, MeasurementUnit.Cup, ["skim milk"])));

        outcome.Inserted.Should().Be(2);
        outcome.Updated.Should().Be(0);

        var onion = await ReadAsync("onion");
        onion.DisplayName.Should().Be("Onion");
        onion.Category.Should().Be(IngredientCategory.Produce);
        onion.DefaultUnit.Should().Be(MeasurementUnit.Piece);
        onion.Aliases.Should().Equal("onions");
    }

    [Fact]
    public async Task InsertsTheStapleFlagFromTheArtefact()
    {
        await using var context = db.CreateDbContext();

        await IngredientCatalogueFileSeeder.SeedAsync(context, Catalogue(
            Entry("salt", isStaple: true),
            Entry("onion")));

        (await ReadAsync("salt")).IsStaple.Should().BeTrue();
        (await ReadAsync("onion")).IsStaple.Should().BeFalse();
    }

    [Fact]
    public async Task ReRunningIsANoOp()
    {
        var catalogue = Catalogue(Entry("onion", "Onion", aliases: ["onions"]));

        await using (var first = db.CreateDbContext())
            await IngredientCatalogueFileSeeder.SeedAsync(first, catalogue);

        await using var second = db.CreateDbContext();
        var outcome = await IngredientCatalogueFileSeeder.SeedAsync(second, catalogue);

        outcome.Inserted.Should().Be(0);
        outcome.Updated.Should().Be(0);
        outcome.Unchanged.Should().Be(1);

        (await second.Ingredients.CountAsync()).Should().Be(1);
    }

    // ── Fill nulls, never overwrite ───────────────────────────────────────────

    [Fact]
    public async Task FillsANullDefaultUnit()
    {
        await SeedRowAsync(new Ingredient
        {
            Id = Guid.NewGuid(), Name = "onion", DisplayName = "Onion",
            Category = IngredientCategory.Produce, DefaultUnit = null,
        });

        await using var context = db.CreateDbContext();
        await IngredientCatalogueFileSeeder.SeedAsync(context, Catalogue(
            Entry("onion", "Onion", IngredientCategory.Produce, MeasurementUnit.Piece)));

        (await ReadAsync("onion")).DefaultUnit.Should().Be(MeasurementUnit.Piece);
    }

    [Fact]
    public async Task NeverOverwritesACorrectedValue()
    {
        // Someone decided an onion is bought by weight here, and the artefact does not get to argue.
        await SeedRowAsync(new Ingredient
        {
            Id = Guid.NewGuid(), Name = "onion", DisplayName = "Brown Onion",
            Category = IngredientCategory.Produce, DefaultUnit = MeasurementUnit.Gram,
        });

        await using var context = db.CreateDbContext();
        await IngredientCatalogueFileSeeder.SeedAsync(context, Catalogue(
            Entry("onion", "Onion", IngredientCategory.Produce, MeasurementUnit.Piece)));

        var row = await ReadAsync("onion");
        row.DefaultUnit.Should().Be(MeasurementUnit.Gram);
        row.DisplayName.Should().Be("Brown Onion");
    }

    [Fact]
    public async Task ReplacesTheOtherCategoryPlaceholderButNotARealJudgement()
    {
        // "OTHER" is the column default, so a row carrying it was never categorised rather than categorised as miscellaneous.
        await SeedRowAsync(new Ingredient
        {
            Id = Guid.NewGuid(), Name = "peanut butter", DisplayName = "Peanut Butter",
            Category = IngredientCategory.Other,
        });
        await SeedRowAsync(new Ingredient
        {
            Id = Guid.NewGuid(), Name = "chicken broth", DisplayName = "Chicken Broth",
            Category = IngredientCategory.Canned,
        });

        await using var context = db.CreateDbContext();
        await IngredientCatalogueFileSeeder.SeedAsync(context, Catalogue(
            Entry("peanut butter", "Peanut Butter", IngredientCategory.Condiments),
            Entry("chicken broth", "Chicken Broth", IngredientCategory.Beverages)));

        (await ReadAsync("peanut butter")).Category.Should().Be(IngredientCategory.Condiments);
        (await ReadAsync("chicken broth")).Category.Should().Be(IngredientCategory.Canned);
    }

    [Fact]
    public async Task LeavesAStapleTheUserClearedCleared()
    {
        // A flag cannot record that it was never set, so the row's value always counts as a judgement.
        await SeedRowAsync(new Ingredient
        {
            Id = Guid.NewGuid(), Name = "salt", DisplayName = "Salt",
            Category = IngredientCategory.BakingSpices, IsStaple = false,
        });

        await using var context = db.CreateDbContext();
        await IngredientCatalogueFileSeeder.SeedAsync(context, Catalogue(
            Entry("salt", "Salt", IngredientCategory.BakingSpices, isStaple: true)));

        (await ReadAsync("salt")).IsStaple.Should().BeFalse();
    }

    [Fact]
    public async Task MergesAliasesRatherThanReplacingThem()
    {
        // A row created at runtime by a scrape is exactly the case this protects, since taking its aliases away would break lookups that already work.
        await SeedRowAsync(new Ingredient
        {
            Id = Guid.NewGuid(), Name = "chickpeas", DisplayName = "Chickpeas",
            Category = IngredientCategory.Canned, Aliases = ["ceci beans"],
        });

        await using var context = db.CreateDbContext();
        await IngredientCatalogueFileSeeder.SeedAsync(context, Catalogue(
            Entry("chickpeas", "Chickpeas", IngredientCategory.Canned,
                aliases: ["garbanzo beans"])));

        (await ReadAsync("chickpeas")).Aliases
            .Should().BeEquivalentTo(["ceci beans", "garbanzo beans"]);
    }

    // ── --force ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ForceReAppliesTheArtefactOverExistingValues()
    {
        await SeedRowAsync(new Ingredient
        {
            Id = Guid.NewGuid(), Name = "onion", DisplayName = "Brown Onion",
            Category = IngredientCategory.Dairy, DefaultUnit = MeasurementUnit.Gram,
            Aliases = ["stale alias"],
        });

        await using var context = db.CreateDbContext();
        var outcome = await IngredientCatalogueFileSeeder.SeedAsync(context, Catalogue(
            Entry("onion", "Onion", IngredientCategory.Produce, MeasurementUnit.Piece, ["onions"])),
            force: true);

        outcome.Updated.Should().Be(1);
        outcome.Forced.Should().BeTrue();

        var row = await ReadAsync("onion");
        row.DisplayName.Should().Be("Onion");
        row.Category.Should().Be(IngredientCategory.Produce);
        row.DefaultUnit.Should().Be(MeasurementUnit.Piece);
        row.Aliases.Should().Equal("onions");
    }

    [Fact]
    public async Task ForceReAppliesTheStapleFlag()
    {
        await SeedRowAsync(new Ingredient
        {
            Id = Guid.NewGuid(), Name = "salt", DisplayName = "Salt",
            Category = IngredientCategory.BakingSpices, IsStaple = false,
        });
        await SeedRowAsync(new Ingredient
        {
            Id = Guid.NewGuid(), Name = "onion", DisplayName = "Onion",
            Category = IngredientCategory.Produce, IsStaple = true,
        });

        await using var context = db.CreateDbContext();
        await IngredientCatalogueFileSeeder.SeedAsync(context, Catalogue(
            Entry("salt", "Salt", IngredientCategory.BakingSpices, isStaple: true),
            Entry("onion", "Onion", IngredientCategory.Produce)),
            force: true);

        (await ReadAsync("salt")).IsStaple.Should().BeTrue();
        (await ReadAsync("onion")).IsStaple.Should().BeFalse();
    }

    [Fact]
    public async Task ForceDoesNotDeleteRowsTheArtefactDoesNotMention()
    {
        // A refresh re-applies the artefact but is not a reset, because deleting a user's own ingredient would cascade into every recipe that uses it.
        await SeedRowAsync(new Ingredient
        {
            Id = Guid.NewGuid(), Name = "kohlrabi", DisplayName = "Kohlrabi",
            Category = IngredientCategory.Produce,
        });

        await using var context = db.CreateDbContext();
        await IngredientCatalogueFileSeeder.SeedAsync(
            context, Catalogue(Entry("onion", "Onion")), force: true);

        (await context.Ingredients.AnyAsync(i => i.Name == "kohlrabi")).Should().BeTrue();
    }

    // ── Validation at seed time ───────────────────────────────────────────────

    [Fact]
    public async Task RefusesAnArtefactThatContradictsItself()
    {
        // The file is hand-editable by design, so the rules are checked again here rather than trusted from build time.
        await using var context = db.CreateDbContext();

        var seed = async () => await IngredientCatalogueFileSeeder.SeedAsync(context, Catalogue(
            Entry("chickpeas", aliases: ["garbanzo beans"]),
            Entry("lentils",   aliases: ["garbanzo beans"])));

        await seed.Should().ThrowAsync<CatalogueValidationException>()
            .Where(ex => ex.Message.Contains("claimed by both"));

        (await context.Ingredients.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RefusesAnArtefactWithAnUnstorableUnit()
    {
        await using var context = db.CreateDbContext();

        var seed = async () => await IngredientCatalogueFileSeeder.SeedAsync(
            context, Catalogue(Entry("garlic", defaultUnit: "clove")));

        await seed.Should().ThrowAsync<CatalogueValidationException>();
        (await context.Ingredients.CountAsync()).Should().Be(0);
    }

    // ── Interaction with the density seeder ───────────────────────────────────

    [Fact]
    public async Task DensitiesStillAttachThroughTheNewNamesOrTheirAliases()
    {
        // The density table was written against the old starter catalogue, so a name-only match would silently stop attaching densities once spellings became aliases.
        await using var context = db.CreateDbContext();

        await IngredientCatalogueFileSeeder.SeedAsync(context, Catalogue(
            Entry("all-purpose flour", "All-Purpose Flour", IngredientCategory.DryGoods,
                MeasurementUnit.Gram, ["plain flour", "flour"])));

        await IngredientDensitySeeder.SeedAsync(context);

        (await ReadAsync("all-purpose flour")).GramsPerMillilitre.Should().NotBeNull();
    }
}
