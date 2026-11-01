using RecipeApp.API.Data;
using RecipeApp.API.Enums;
using RecipeApp.Tests.Helpers;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Data;

/// <summary>The curated table itself, with no database, guarding the data most likely to drift as the catalogue grows.</summary>
public class IngredientDensityTableTests
{
    [Fact]
    public void Table_IsWithinTheTargetedSize()
    {
        // Far fewer entries leaves the corpus uncovered, and far more usually means someone started guessing.
        IngredientDensitySeeder.Rows.Should().HaveCountGreaterThanOrEqualTo(40);
        IngredientDensitySeeder.Rows.Should().HaveCountLessThanOrEqualTo(60);
    }

    [Fact]
    public void EveryRowHasAPlausibleDensityAndACitation()
    {
        foreach (var row in IngredientDensitySeeder.Rows)
        {
            row.GramsPerMillilitre.Should().BeInRange(0.01m, 3m,
                "'{0}' must have a kitchen-plausible density", row.ReferenceName);
            row.CatalogueNames.Should().NotBeEmpty(
                "'{0}' must apply to at least one catalogue name", row.ReferenceName);
            row.Source.Should().NotBeNullOrWhiteSpace(
                "'{0}' must be traceable to a source", row.ReferenceName);
        }
    }

    [Fact]
    public void CatalogueNamesAreNormalisedAndUnique()
    {
        // Catalogue names are stored lowercase and trimmed, and asserting uniqueness here names the duplicate instead of failing in a type initialiser.
        var allNames = IngredientDensitySeeder.Rows.SelectMany(r => r.CatalogueNames).ToList();

        allNames.Should().OnlyContain(n => n == n.Trim().ToLowerInvariant());
        allNames.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void DensityIsDerivedFromGramsPerCupUsingTheAppsOwnCupDefinition()
    {
        foreach (var row in IngredientDensitySeeder.Rows)
        {
            var oneCupInGrams = MeasurementUnit.MillilitresPerCup * row.GramsPerMillilitre;
            oneCupInGrams.Should().BeApproximately(row.GramsPerCup, 0.05m,
                "one cup of '{0}' must round-trip to its published weight", row.ReferenceName);
        }
    }

    [Theory]
    // Every dry good in the starter catalogue must be covered, or the app ships with a density table that gives its own seed data nothing.
    [InlineData("plain flour")]
    [InlineData("rice")]
    [InlineData("breadcrumbs")]
    [InlineData("salt")]
    [InlineData("butter")]
    [InlineData("parmesan cheese")]
    [InlineData("cheddar cheese")]
    public void StarterCatalogueDryGoodsAreCovered(string catalogueName)
    {
        IngredientDensitySeeder.ByCatalogueName.Should().ContainKey(catalogueName);
    }

    [Theory]
    // Bought by volume, so a gram figure would be correct physics and useless cooking.
    [InlineData("water")]
    [InlineData("milk")]
    [InlineData("cream")]
    [InlineData("olive oil")]
    [InlineData("beef stock")]
    [InlineData("chicken stock")]
    [InlineData("honey")]
    [InlineData("maple syrup")]
    [InlineData("peanut butter")]
    // Packing-dominated, so no single figure is true.
    [InlineData("spinach")]
    [InlineData("lettuce")]
    public void LiquidsAndPackingDominatedIngredientsAreDeliberatelyAbsent(string catalogueName)
    {
        IngredientDensitySeeder.ByCatalogueName.Should().NotContainKey(catalogueName);
    }

    [Fact]
    public void PackedAndLooseBrownSugarAreSeparateEntries()
    {
        // The two pack states differ by about 50%, so they are distinct catalogue entries rather than one averaged guess.
        var packed = IngredientDensitySeeder.ByCatalogueName["packed brown sugar"];
        var loose  = IngredientDensitySeeder.ByCatalogueName["loose brown sugar"];

        packed.Should().BeGreaterThan(loose);

        // A bare "brown sugar" follows the standard baking convention of firmly packed.
        IngredientDensitySeeder.ByCatalogueName["brown sugar"].Should().Be(packed);
    }
}

/// <summary>Seeding behaviour against a real database.</summary>
[Collection("Database")]
public class IngredientDensitySeederTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SeedAsync_AssignsDensitiesByCatalogueAlias()
    {
        await using var ctx = db.CreateDbContext();
        // The starter catalogue says "plain flour", not the reference name "all-purpose flour".
        ctx.Ingredients.Add(TestDataBuilder.Ingredient("plain flour", "Plain Flour", IngredientCategory.DryGoods));
        await ctx.SaveChangesAsync();

        var updated = await IngredientDensitySeeder.SeedAsync(ctx);

        updated.Should().Be(1);
        var flour = ctx.Ingredients.Single(i => i.Name == "plain flour");
        flour.GramsPerMillilitre.Should().Be(0.5m);
    }

    [Fact]
    public async Task SeedAsync_LeavesUncoveredIngredientsNull()
    {
        await using var ctx = db.CreateDbContext();
        ctx.Ingredients.Add(TestDataBuilder.Ingredient("spinach", "Spinach", IngredientCategory.Produce));
        await ctx.SaveChangesAsync();

        var updated = await IngredientDensitySeeder.SeedAsync(ctx);

        updated.Should().Be(0);
        ctx.Ingredients.Single(i => i.Name == "spinach").GramsPerMillilitre.Should().BeNull();
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        await using var ctx = db.CreateDbContext();
        ctx.Ingredients.Add(TestDataBuilder.Ingredient("rice", "Rice", IngredientCategory.DryGoods));
        await ctx.SaveChangesAsync();

        (await IngredientDensitySeeder.SeedAsync(ctx)).Should().Be(1);
        (await IngredientDensitySeeder.SeedAsync(ctx)).Should().Be(0);
    }

    [Fact]
    public async Task SeedAsync_DoesNotOverwriteAHandCorrectedDensity()
    {
        await using var ctx = db.CreateDbContext();
        ctx.Ingredients.Add(TestDataBuilder.Ingredient(
            "plain flour", "Plain Flour", IngredientCategory.DryGoods, "g", gramsPerMillilitre: 0.42m));
        await ctx.SaveChangesAsync();

        var updated = await IngredientDensitySeeder.SeedAsync(ctx);

        updated.Should().Be(0);
        ctx.Ingredients.Single(i => i.Name == "plain flour").GramsPerMillilitre.Should().Be(0.42m);
    }

    [Fact]
    public async Task SeedAsync_CoversCatalogueEntriesAddedLater()
    {
        // Safe to re-run after a harvest grows the catalogue.
        await using var ctx = db.CreateDbContext();
        ctx.Ingredients.Add(TestDataBuilder.Ingredient("rice", "Rice", IngredientCategory.DryGoods));
        await ctx.SaveChangesAsync();
        await IngredientDensitySeeder.SeedAsync(ctx);

        ctx.Ingredients.Add(TestDataBuilder.Ingredient("rolled oats", "Rolled Oats", IngredientCategory.DryGoods));
        await ctx.SaveChangesAsync();

        (await IngredientDensitySeeder.SeedAsync(ctx)).Should().Be(1);
        ctx.Ingredients.Single(i => i.Name == "rolled oats").GramsPerMillilitre.Should().Be(0.375m);
    }
}
