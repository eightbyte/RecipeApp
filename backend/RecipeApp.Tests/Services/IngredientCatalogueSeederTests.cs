using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RecipeApp.API.Enums;
using RecipeApp.API.Services;
using RecipeApp.Tests.Helpers;
using RecipeApp.Tests.Infrastructure;
using RecipeApp.Tests.Services.Llm;

namespace RecipeApp.Tests.Services;

[Collection("Database")]
public class IngredientCatalogueSeederTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static JsonNode MakeBatch(params (string name, string display, string category, string unit)[] items)
    {
        var arr = new JsonArray(items.Select(i => (JsonNode)JsonNode.Parse($$"""
            {
              "name": "{{i.name}}",
              "display_name": "{{i.display}}",
              "category": "{{i.category}}",
              "default_unit": "{{i.unit}}"
            }
            """)!).ToArray());

        return JsonNode.Parse("{}")!.AsObject().Also(o => o["ingredients"] = arr);
    }

    private IngredientCatalogueSeeder BuildSeeder(params JsonNode[] responses) =>
        new(
            llm: new FakeLlmStructuredClient(responses),
            db: db.CreateDbContext(),
            logger: NullLogger<IngredientCatalogueSeeder>.Instance);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedAsync_InsertsGeneratedIngredients()
    {
        var batch = MakeBatch(
            ("garlic", "Garlic", IngredientCategory.Produce, "pcs"),
            ("butter", "Butter", IngredientCategory.Dairy, "g"));

        var seeder = BuildSeeder(batch);
        await seeder.SeedAsync(targetCount: 2);

        await using var ctx = db.CreateDbContext();
        var names = await ctx.Ingredients.Select(i => i.Name).ToListAsync();
        names.Should().Contain("garlic");
        names.Should().Contain("butter");
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_SecondRunInsertsNothing()
    {
        var batch = MakeBatch(("basil", "Basil", IngredientCategory.Produce, "g"));

        // First run
        await BuildSeeder(batch).SeedAsync(targetCount: 1);

        // Second run with same data
        var batch2 = MakeBatch(("basil", "Basil", IngredientCategory.Produce, "g"));
        await BuildSeeder(batch2).SeedAsync(targetCount: 1);

        await using var ctx = db.CreateDbContext();
        var count = await ctx.Ingredients.CountAsync(i => i.Name == "basil");
        count.Should().Be(1);
    }

    [Fact]
    public async Task SeedAsync_InvalidCategory_FallsBackToKeywordCategorisation()
    {
        var batch = MakeBatch(("chicken breast", "Chicken Breast", "NOT_A_CATEGORY", "g"));
        await BuildSeeder(batch).SeedAsync(targetCount: 1);

        await using var ctx = db.CreateDbContext();
        var ing = await ctx.Ingredients.SingleAsync(i => i.Name == "chicken breast");
        ing.Category.Should().Be(IngredientCategory.MeatSeafood);
    }

    [Fact]
    public async Task SeedAsync_InvalidUnit_DefaultsToPcs()
    {
        var batch = MakeBatch(("thyme", "Thyme", IngredientCategory.Condiments, "bunch"));
        await BuildSeeder(batch).SeedAsync(targetCount: 1);

        await using var ctx = db.CreateDbContext();
        var ing = await ctx.Ingredients.SingleAsync(i => i.Name == "thyme");
        ing.DefaultUnit.Should().Be("pcs");
    }

    [Fact]
    public async Task SeedAsync_DeduplicatesWithinBatch()
    {
        // Same name twice in one batch
        var batch = MakeBatch(
            ("oregano", "Oregano", IngredientCategory.Condiments, "tsp"),
            ("oregano", "Oregano", IngredientCategory.Condiments, "tsp"));

        await BuildSeeder(batch).SeedAsync(targetCount: 2);

        await using var ctx = db.CreateDbContext();
        var count = await ctx.Ingredients.CountAsync(i => i.Name == "oregano");
        count.Should().Be(1);
    }

    [Fact]
    public async Task SeedAsync_DoesNotClobberExistingRows()
    {
        await using var insertCtx = db.CreateDbContext();
        var existing = TestDataBuilder.Ingredient("onion", "Onion", IngredientCategory.Produce, "pcs");
        insertCtx.Ingredients.Add(existing);
        await insertCtx.SaveChangesAsync();

        // Seeder tries to add "onion" again
        var batch = MakeBatch(("onion", "Onion (new)", IngredientCategory.Dairy, "ml"));
        await BuildSeeder(batch).SeedAsync(targetCount: 1);

        await using var ctx = db.CreateDbContext();
        var ing = await ctx.Ingredients.SingleAsync(i => i.Name == "onion");
        ing.DisplayName.Should().Be("Onion");      // original
        ing.Category.Should().Be(IngredientCategory.Produce); // original
    }
}

// ── Helper extension to allow object initialiser fluent style ────────────────

file static class ObjectExtensions
{
    public static T Also<T>(this T self, Action<T> action) { action(self); return self; }
}
