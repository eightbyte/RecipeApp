using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecipeApp.API.Enums;
using RecipeApp.API.Services;
using RecipeApp.API.Services.Llm;
using RecipeApp.Tests.Helpers;
using RecipeApp.Tests.Infrastructure;
using RecipeApp.Tests.Services.Llm;

namespace RecipeApp.Tests.Services;

[Collection("Database")]
public class RecipeScrapeServiceMatchingTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private RecipeScrapeService BuildService(params JsonNode[] llmResponses)
    {
        var opts    = Options.Create(new RecipeScrapingOptions { MatchConfidenceThreshold = 0.8 });
        var fakeLlm = new FakeLlmStructuredClient(llmResponses);
        return new RecipeScrapeService(
            httpClientFactory: null!,     // not used in NormaliseAsync
            options: opts,
            llm: fakeLlm,
            db: db.CreateDbContext(),
            headlessRenderer: null!,      // not used in NormaliseAsync
            measurementConverter: new MeasurementConverter(Options.Create(new MeasurementOptions())),
            logger: NullLogger<RecipeScrapeService>.Instance);
    }

    private static RecipeApp.API.Services.RecipeScrapeService.ExtractedRecipe MakeRecipe(
        params (string name, string display)[] ingredients) =>
        new(
            Name: "Test Recipe",
            Description: null,
            Servings: 4,
            Ingredients: ingredients
                .Select(i => new RecipeApp.API.Services.RecipeScrapeService.ExtractedIngredient(
                    i.name, i.display, 100.0, "g", null))
                .ToList(),
            Steps:
            [
                new RecipeApp.API.Services.RecipeScrapeService.ExtractedStep(1, "Cook.", [])
            ]
        );

    /// <summary>A one-ingredient recipe stating a specific measurement, as a source page would.</summary>
    private static RecipeApp.API.Services.RecipeScrapeService.ExtractedRecipe MakeRecipeMeasured(
        string name, string display, double amount, string unit) =>
        new(
            Name: "Test Recipe",
            Description: null,
            Servings: 4,
            Ingredients:
            [
                new RecipeApp.API.Services.RecipeScrapeService.ExtractedIngredient(
                    name, display, amount, unit, null)
            ],
            Steps:
            [
                new RecipeApp.API.Services.RecipeScrapeService.ExtractedStep(1, "Cook.", [])
            ]
        );

    private static JsonNode ConfidentMatchTo(int candidateIndex) => JsonNode.Parse($$"""
        {
          "results": [
            {
              "scraped_index": 0,
              "candidate_index": {{candidateIndex}},
              "confidence": 0.95,
              "suggested_category": "DRY_GOODS",
              "suggested_default_unit": "g"
            }
          ]
        }
        """)!;

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NormaliseAsync_ExactMatch_ResolvesByNameNoLlmCall()
    {
        await using var ctx = db.CreateDbContext();
        var scallion = TestDataBuilder.Ingredient("scallion", "Scallion", IngredientCategory.Produce);
        ctx.Ingredients.Add(scallion);
        await ctx.SaveChangesAsync();

        // No LLM response needed — exact match short-circuits
        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(MakeRecipe(("scallion", "Scallion")), "https://x", default);

        preview.Ingredients.Should().HaveCount(1);
        var ing = preview.Ingredients[0];
        ing.IsNew.Should().BeFalse();
        ing.IngredientId.Should().Be(scallion.Id);
    }

    [Fact]
    public async Task NormaliseAsync_SynonymAboveThreshold_ResolvesToExistingId()
    {
        await using var ctx = db.CreateDbContext();
        var scallion = TestDataBuilder.Ingredient("scallion", "Scallion", IngredientCategory.Produce);
        ctx.Ingredients.Add(scallion);
        await ctx.SaveChangesAsync();

        // LLM semantic match: candidate_index 0 with confidence 0.95
        var matchResponse = JsonNode.Parse("""
            {
              "results": [
                {
                  "scraped_index": 0,
                  "candidate_index": 0,
                  "confidence": 0.95,
                  "suggested_category": "PRODUCE",
                  "suggested_default_unit": "pcs"
                }
              ]
            }
            """)!;

        var svc     = BuildService(matchResponse);
        var preview = await svc.NormaliseAsync(MakeRecipe(("spring onion", "Spring Onion")), "https://x", default);

        preview.Ingredients.Should().HaveCount(1);
        var ing = preview.Ingredients[0];
        ing.IsNew.Should().BeFalse();
        // The ID came from the index→ID map, not from a GUID the model emitted
        ing.IngredientId.Should().Be(scallion.Id);
    }

    [Fact]
    public async Task NormaliseAsync_SynonymBelowThreshold_RemainsNew()
    {
        await using var ctx = db.CreateDbContext();
        var scallion = TestDataBuilder.Ingredient("scallion", "Scallion", IngredientCategory.Produce);
        ctx.Ingredients.Add(scallion);
        await ctx.SaveChangesAsync();

        var matchResponse = JsonNode.Parse("""
            {
              "results": [
                {
                  "scraped_index": 0,
                  "candidate_index": 0,
                  "confidence": 0.5,
                  "suggested_category": "PRODUCE",
                  "suggested_default_unit": "pcs"
                }
              ]
            }
            """)!;

        var svc     = BuildService(matchResponse);
        var preview = await svc.NormaliseAsync(MakeRecipe(("spring onion", "Spring Onion")), "https://x", default);

        preview.Ingredients[0].IsNew.Should().BeTrue();
        preview.Ingredients[0].IngredientId.Should().BeNull();
    }

    [Fact]
    public async Task NormaliseAsync_InvalidCategoryFromModel_FallsBackToKeywordCategorisation()
    {
        await using var ctx = db.CreateDbContext();
        // Seed any ingredient so semantic match pass triggers
        ctx.Ingredients.Add(TestDataBuilder.Ingredient("onion", "Onion", IngredientCategory.Produce));
        await ctx.SaveChangesAsync();

        var matchResponse = JsonNode.Parse("""
            {
              "results": [
                {
                  "scraped_index": 0,
                  "candidate_index": null,
                  "confidence": 0.0,
                  "suggested_category": "INVALID_CATEGORY",
                  "suggested_default_unit": "g"
                }
              ]
            }
            """)!;

        var svc     = BuildService(matchResponse);
        // "chicken breast" should be categorised as MEAT_SEAFOOD by keyword fallback
        var preview = await svc.NormaliseAsync(MakeRecipe(("chicken breast", "Chicken Breast")), "https://x", default);

        preview.Ingredients[0].SuggestedCategory.Should().Be(IngredientCategory.MeatSeafood);
    }

    [Fact]
    public async Task NormaliseAsync_NoCatalogueEntries_SkipsSemanticMatchAndSetsKeywordCategory()
    {
        // Empty DB — semantic match skipped entirely
        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(MakeRecipe(("salmon fillet", "Salmon Fillet")), "https://x", default);

        preview.Ingredients[0].IsNew.Should().BeTrue();
        preview.Ingredients[0].SuggestedCategory.Should().Be(IngredientCategory.MeatSeafood);
    }

    [Fact]
    public async Task NormaliseAsync_OutOfBoundsCandidateIndex_IsIgnored()
    {
        await using var ctx = db.CreateDbContext();
        ctx.Ingredients.Add(TestDataBuilder.Ingredient("onion", "Onion", IngredientCategory.Produce));
        await ctx.SaveChangesAsync();

        // candidate_index = 999 is out of range — should be treated as no match
        var matchResponse = JsonNode.Parse("""
            {
              "results": [
                {
                  "scraped_index": 0,
                  "candidate_index": 999,
                  "confidence": 0.99,
                  "suggested_category": "PRODUCE"
                }
              ]
            }
            """)!;

        var svc     = BuildService(matchResponse);
        var preview = await svc.NormaliseAsync(MakeRecipe(("spring onion", "Spring Onion")), "https://x", default);

        preview.Ingredients[0].IsNew.Should().BeTrue();
    }

    // ── Source measurement and cup resolution (Phase 8.5.1 §5.5, §6) ──────────

    [Fact]
    public async Task NormaliseAsync_RecordsTheSourceMeasurementVerbatim()
    {
        await using var ctx = db.CreateDbContext();
        ctx.Ingredients.Add(TestDataBuilder.Ingredient("plain flour", "Plain Flour", IngredientCategory.DryGoods));
        await ctx.SaveChangesAsync();

        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeMeasured("plain flour", "Plain Flour", 2.0, "cups"), "https://x", default);

        var ing = preview.Ingredients.Single();
        ing.SourceAmount.Should().Be(2m);
        ing.SourceUnit.Should().Be("cups");
    }

    [Fact]
    public async Task NormaliseAsync_RecordsSourceEvenWhenNothingIsConverted()
    {
        await using var ctx = db.CreateDbContext();
        ctx.Ingredients.Add(TestDataBuilder.Ingredient("plain flour", "Plain Flour", IngredientCategory.DryGoods));
        await ctx.SaveChangesAsync();

        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeMeasured("plain flour", "Plain Flour", 500.0, "g"), "https://x", default);

        var ing = preview.Ingredients.Single();
        ing.Amount.Should().Be(500m);
        ing.Unit.Should().Be("g");
        ing.SourceAmount.Should().Be(500m);
        ing.SourceUnit.Should().Be("g");
    }

    [Fact]
    public async Task NormaliseAsync_ExactMatchWithDensity_ResolvesCupsToGrams()
    {
        await using var ctx = db.CreateDbContext();
        ctx.Ingredients.Add(TestDataBuilder.Ingredient(
            "plain flour", "Plain Flour", IngredientCategory.DryGoods, "g", gramsPerMillilitre: 0.5m));
        await ctx.SaveChangesAsync();

        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeMeasured("plain flour", "Plain Flour", 2.0, "cups"), "https://x", default);

        var ing = preview.Ingredients.Single();
        ing.Amount.Should().Be(240m);        // 2 x 240 ml x 0.5 g/ml
        ing.Unit.Should().Be("g");
        ing.SourceUnit.Should().Be("cups");  // the conversion stays auditable
    }

    [Fact]
    public async Task NormaliseAsync_ExactMatchWithoutDensity_KeepsTheCup()
    {
        await using var ctx = db.CreateDbContext();
        ctx.Ingredients.Add(TestDataBuilder.Ingredient(
            "spinach", "Spinach", IngredientCategory.Produce, "g"));
        await ctx.SaveChangesAsync();

        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeMeasured("spinach", "Spinach", 2.0, "cups"), "https://x", default);

        var ing = preview.Ingredients.Single();
        ing.Amount.Should().Be(2m);
        ing.Unit.Should().Be("cup");   // canonicalised, not converted
    }

    [Fact]
    public async Task NormaliseAsync_SemanticMatchWithDensity_ResolvesCupsToGrams()
    {
        // Pass 2 resolves the IngredientId after the LLM call, so cup resolution has to run
        // there too — not only inline with the pass-1 exact match.
        await using var ctx = db.CreateDbContext();
        ctx.Ingredients.Add(TestDataBuilder.Ingredient(
            "plain flour", "Plain Flour", IngredientCategory.DryGoods, "g", gramsPerMillilitre: 0.5m));
        await ctx.SaveChangesAsync();

        var svc     = BuildService(ConfidentMatchTo(0));
        var preview = await svc.NormaliseAsync(
            MakeRecipeMeasured("all-purpose flour", "All-Purpose Flour", 2.0, "cups"), "https://x", default);

        var ing = preview.Ingredients.Single();
        ing.IsNew.Should().BeFalse();
        ing.Amount.Should().Be(240m);
        ing.Unit.Should().Be("g");
    }

    [Fact]
    public async Task NormaliseAsync_UnmatchedIngredient_KeepsTheCup()
    {
        // An ingredient with no catalogue row has no density by construction.
        await using var ctx = db.CreateDbContext();
        ctx.Ingredients.Add(TestDataBuilder.Ingredient("rice", "Rice", IngredientCategory.DryGoods));
        await ctx.SaveChangesAsync();

        var svc     = BuildService();   // no LLM response: semantic matching falls back
        var preview = await svc.NormaliseAsync(
            MakeRecipeMeasured("mixed salad greens", "Mixed Salad Greens", 3.0, "cups"), "https://x", default);

        var ing = preview.Ingredients.Single();
        ing.IsNew.Should().BeTrue();
        ing.Amount.Should().Be(3m);
        ing.Unit.Should().Be("cup");
    }

    [Fact]
    public async Task NormaliseAsync_DoesNotThrowOnAnUnrecognisedUnit()
    {
        // The interactive preview lets a human fix "clove" before confirming; failing the whole
        // scrape over one unit would be a regression (Phase 8.5.1 §5.5).
        await using var ctx = db.CreateDbContext();
        ctx.Ingredients.Add(TestDataBuilder.Ingredient("garlic", "Garlic", IngredientCategory.Produce));
        await ctx.SaveChangesAsync();

        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeMeasured("garlic", "Garlic", 3.0, "cloves"), "https://x", default);

        var ing = preview.Ingredients.Single();
        ing.Unit.Should().Be("cloves");
        ing.SourceUnit.Should().Be("cloves");
    }
}
