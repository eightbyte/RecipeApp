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
            modelServer: null!,           // not used in NormaliseAsync
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
        MakeRecipeStated(name, display, amount, unit);

    /// <summary>A one-ingredient recipe as the model read it, where either half of the measurement may be missing.</summary>
    private static RecipeApp.API.Services.RecipeScrapeService.ExtractedRecipe MakeRecipeStated(
        string name, string display, double? amount, string? unit, string? notes = null) =>
        new(
            Name: "Test Recipe",
            Description: null,
            Servings: 4,
            Ingredients:
            [
                new RecipeApp.API.Services.RecipeScrapeService.ExtractedIngredient(
                    name, display, amount, unit, notes)
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
    public async Task NormaliseAsync_KnownAlias_ResolvesOnPassOneWithNoLlmCall()
    {
        // A synonym the catalogue already records is a dictionary hit rather than a model call.
        await using var ctx = db.CreateDbContext();
        var chickpeas = TestDataBuilder.Ingredient("chickpeas", "Chickpeas", IngredientCategory.Canned);
        chickpeas.Aliases = ["garbanzo beans", "chick peas"];
        ctx.Ingredients.Add(chickpeas);
        await ctx.SaveChangesAsync();

        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipe(("garbanzo beans", "Garbanzo Beans")), "https://x", default);

        var ing = preview.Ingredients.Should().ContainSingle().Which;
        ing.IsNew.Should().BeFalse();
        ing.IngredientId.Should().Be(chickpeas.Id);
    }

    [Fact]
    public async Task NormaliseAsync_ANameOutranksAnotherEntrysAlias()
    {
        // The artefact's validation forbids this collision, and this is what makes the outcome defined anyway if a hand-edited row introduces one.
        await using var ctx = db.CreateDbContext();
        var tomato = TestDataBuilder.Ingredient("tomato", "Tomato", IngredientCategory.Produce);
        tomato.Aliases = ["diced tomatoes"];
        var diced = TestDataBuilder.Ingredient("diced tomatoes", "Diced Tomatoes", IngredientCategory.Canned);
        ctx.Ingredients.AddRange(tomato, diced);
        await ctx.SaveChangesAsync();

        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipe(("diced tomatoes", "Diced Tomatoes")), "https://x", default);

        preview.Ingredients.Should().ContainSingle().Which.IngredientId.Should().Be(diced.Id);
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

    // ── Source measurement and cup resolution ─────────────────────────────────

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
        // Pass 2 resolves the IngredientId after the LLM call, so cup resolution has to run there too and not only with the pass-1 exact match.
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
        // The interactive preview lets a human fix "can" before confirming, so failing the whole scrape over one unit would be a regression.
        await using var ctx = db.CreateDbContext();
        ctx.Ingredients.Add(TestDataBuilder.Ingredient("tomato", "Tomato", IngredientCategory.Canned));
        await ctx.SaveChangesAsync();

        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeMeasured("tomato", "Tomato", 1.0, "can"), "https://x", default);

        var ing = preview.Ingredients.Single();
        ing.Unit.Should().Be("can");
        ing.SourceUnit.Should().Be("can");
    }

    // ── Counts ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NormaliseAsync_AnAmountWithNoUnit_IsACountWithNoSource()
    {
        // "2 eggs": nothing is lost by storing pcs, so there is no Source Measurement to keep.
        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeStated("egg", "Egg", amount: 2.0, unit: null), "https://x", default);

        var ing = preview.Ingredients.Single();
        ing.Amount.Should().Be(2m);
        ing.Unit.Should().Be(MeasurementUnit.Piece);
        ing.SourceAmount.Should().BeNull();
        ing.SourceUnit.Should().BeNull();
    }

    [Fact]
    public async Task NormaliseAsync_ACountWord_IsACountThatKeepsItsWording()
    {
        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeStated("garlic", "Garlic", amount: 2.0, unit: "cloves"), "https://x", default);

        var ing = preview.Ingredients.Single();
        ing.Amount.Should().Be(2m);
        ing.Unit.Should().Be(MeasurementUnit.Piece);
        ing.SourceAmount.Should().Be(2m);
        ing.SourceUnit.Should().Be("cloves");
    }

    [Theory]
    [InlineData("celery",      "Celery",      1.0, "stick",       null)]
    [InlineData("green onion", "Green Onion", 0.5, "small bunch", "scallions")]
    public async Task NormaliseAsync_StickOrBunch_IsACountThatKeepsItsWording(
        string name, string display, double amount, string unit, string? notes)
    {
        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeStated(name, display, amount, unit, notes), "https://x", default);

        preview.Ingredients.Single().Should().BeEquivalentTo(new
        {
            Amount = (decimal)amount, Unit = MeasurementUnit.Piece,
            SourceAmount = (decimal)amount, SourceUnit = unit, Notes = notes,
        });
    }

    [Fact]
    public async Task NormaliseAsync_AStickOfButter_StillAsksForAUnit()
    {
        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeStated("butter", "Butter", 1.0, "stick"), "https://x", default);

        preview.Ingredients.Single().Unit.Should().Be("stick");
    }

    [Theory]
    [InlineData(1.0, "(6 oz) pkg",  170.097, 6.0, "1 pkg")]
    [InlineData(0.5, "(16 oz) pkg", 226.796, 8.0, "½ pkg")]
    public async Task NormaliseAsync_ASizedPackage_IsItsTotalWeightWithThePackageInNotes(
        double count, string unit, double grams, double ounces, string note)
    {
        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeStated("blueberry", "Blueberries", count, unit), "https://x", default);

        preview.Ingredients.Single().Should().BeEquivalentTo(new
        {
            Amount = (decimal)grams, Unit = MeasurementUnit.Gram,
            SourceAmount = (decimal)ounces, SourceUnit = "oz", Notes = note,
        });
    }

    [Theory]
    [InlineData(1.0, "pkg", "(6 oz)",          170.097, 6.0, "1 pkg")]
    [InlineData(0.5, "pkg", "(16 oz)",         226.796, 8.0, "½ pkg")]
    [InlineData(1.0, "pkg", "(6 oz), rinsed",  170.097, 6.0, "1 pkg, rinsed")]
    [InlineData(1.0, "pkg", "6 oz",            170.097, 6.0, "1 pkg")]
    [InlineData(0.5, "pkg", "16 oz",           226.796, 8.0, "½ pkg")]
    [InlineData(1.0, "pkg", "6-oz, rinsed",    170.097, 6.0, "1 pkg, rinsed")]
    public async Task NormaliseAsync_APackageWhoseSizeTheModelPutInTheNotes_IsReadTheSameWay(
        double count, string unit, string notes, double grams, double ounces, string expectedNotes)
    {
        // The model sometimes splits "1 (6 oz) pkg" into a unit of "pkg" and notes of "(6 oz)".
        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeStated("blueberry", "Blueberries", count, unit, notes), "https://x", default);

        preview.Ingredients.Single().Should().BeEquivalentTo(new
        {
            Amount = (decimal)grams, Unit = MeasurementUnit.Gram,
            SourceAmount = (decimal)ounces, SourceUnit = "oz", Notes = expectedNotes,
        });
    }

    [Fact]
    public async Task NormaliseAsync_BracketedNotesThatStateNoSize_LeaveThePackageAskingForAUnit()
    {
        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeStated("blueberry", "Blueberries", 1.0, "pkg", "(any brand)"), "https://x", default);

        preview.Ingredients.Single().Should().BeEquivalentTo(new { Unit = "pkg", Notes = "(any brand)" });
    }

    [Fact]
    public async Task NormaliseAsync_ASizeInTheNotesOfARealUnit_IsLeftAlone()
    {
        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeStated("milk", "Milk", 1.0, "cup", "8 oz"), "https://x", default);

        preview.Ingredients.Single().Should().BeEquivalentTo(new { Amount = 1m, Unit = "cup", Notes = "8 oz" });
    }

    [Fact]
    public async Task NormaliseAsync_ASizedPackage_LeadsTheNotesItJoins()
    {
        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeStated("tomato", "Tomatoes", 2.0, "(15 ounce) cans", "diced"), "https://x", default);

        preview.Ingredients.Single().Should().BeEquivalentTo(new
        {
            Amount = 850.485m, Unit = MeasurementUnit.Gram,
            SourceAmount = 30m, SourceUnit = "ounce", Notes = "2 cans, diced",
        });
    }

    [Fact]
    public async Task NormaliseAsync_ASizeWord_IsACountWithTheWordInNotes()
    {
        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeStated("egg", "Egg", amount: 2.0, unit: "large"), "https://x", default);

        var ing = preview.Ingredients.Single();
        ing.Amount.Should().Be(2m);
        ing.Unit.Should().Be(MeasurementUnit.Piece);
        ing.Notes.Should().Be("large");
        ing.SourceAmount.Should().BeNull();
        ing.SourceUnit.Should().BeNull();
    }

    [Fact]
    public async Task NormaliseAsync_ASizeWord_LeadsTheNotesItJoins()
    {
        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeStated("egg", "Egg", amount: 2.0, unit: "Large", notes: "beaten"), "https://x", default);

        preview.Ingredients.Single().Notes.Should().Be("Large, beaten");
    }

    [Fact]
    public async Task NormaliseAsync_ASizeWordAlreadyInTheNotes_IsNotRepeated()
    {
        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeStated("egg", "Egg", amount: 2.0, unit: "large", notes: "large, beaten"), "https://x", default);

        preview.Ingredients.Single().Notes.Should().Be("large, beaten");
    }

    [Fact]
    public async Task NormaliseAsync_ASizelessUnit_IsUnquantifiedButKeepsItsSource()
    {
        await using var ctx = db.CreateDbContext();
        ctx.Ingredients.Add(TestDataBuilder.Ingredient("salt", "Salt", IngredientCategory.BakingSpices));
        await ctx.SaveChangesAsync();

        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeMeasured("salt", "Salt", 1.0, "pinch"), "https://x", default);

        var ing = preview.Ingredients.Single();
        ing.Amount.Should().BeNull();
        ing.Unit.Should().BeNull();
        ing.SourceAmount.Should().Be(1m);
        ing.SourceUnit.Should().Be("pinch");
    }

    [Fact]
    public async Task NormaliseAsync_ASizelessUnitWithNoAmount_KeepsItsWordingAsTheSource()
    {
        await using var ctx = db.CreateDbContext();
        ctx.Ingredients.Add(TestDataBuilder.Ingredient("salt", "Salt", IngredientCategory.BakingSpices));
        await ctx.SaveChangesAsync();

        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeStated("salt", "Salt", amount: null, unit: "to taste"), "https://x", default);

        var ing = preview.Ingredients.Single();
        ing.Amount.Should().BeNull();
        ing.Unit.Should().BeNull();
        ing.SourceAmount.Should().BeNull();
        ing.SourceUnit.Should().Be("to taste");
    }

    [Fact]
    public async Task NormaliseAsync_AStorableUnitWithNoAmount_RecordsNoSource()
    {
        // Only a sizeless word carries meaning without a number, so a stray "g" beside a missing amount is dropped.
        await using var ctx = db.CreateDbContext();
        ctx.Ingredients.Add(TestDataBuilder.Ingredient("salt", "Salt", IngredientCategory.BakingSpices));
        await ctx.SaveChangesAsync();

        var svc     = BuildService();
        var preview = await svc.NormaliseAsync(
            MakeRecipeStated("salt", "Salt", amount: null, unit: "g"), "https://x", default);

        var ing = preview.Ingredients.Single();
        ing.SourceAmount.Should().BeNull();
        ing.SourceUnit.Should().BeNull();
    }
}
