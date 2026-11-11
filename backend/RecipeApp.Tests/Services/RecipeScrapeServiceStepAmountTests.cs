using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecipeApp.API.DTOs.Scrape;
using RecipeApp.API.Enums;
using RecipeApp.API.Services;
using RecipeApp.API.Services.Llm;
using RecipeApp.Tests.Helpers;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Services;

/// <summary>How a scrape fills an ingredient row whose list line states no amount from the amounts its steps state, including those a Site Reader supplies.</summary>
[Collection("Database")]
public class RecipeScrapeServiceStepAmountTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private readonly RecordingLogger<RecipeScrapeService> _logger = new();

    private StubLlmStructuredClient _llm = null!;

    private RecipeScrapeService BuildService(JsonNode extraction, params ISiteReader[] siteReaders)
    {
        _llm = new StubLlmStructuredClient(extraction);
        return new RecipeScrapeService(
            httpClientFactory:    StubHttpClientFactory.Returning(MealimePageFixture.Html),
            options:              Options.Create(new RecipeScrapingOptions()),
            llm:                  _llm,
            modelServer:          new StubModelServerStatusProbe(ModelServerState.Ready, "model.gguf"),
            db:                   db.CreateDbContext(),
            headlessRenderer:     new FakeHeadlessRenderer(),
            measurementConverter: new MeasurementConverter(Options.Create(new MeasurementOptions())),
            siteReaders:          siteReaders,
            logger:               _logger);
    }

    /// <summary>What the model read off a page: each ingredient line as stated, and the amounts each step states as (step, ingredient index, amount, unit).</summary>
    private static JsonObject Extraction(
        (string Name, double? Amount, string? Unit)[] ingredients,
        params (int Step, int Index, double? Amount, string? Unit)[] stepAmounts)
    {
        var stepNumbers = stepAmounts.Select(a => a.Step).Append(1).Distinct().Order();
        return new JsonObject
        {
            ["name"]        = "Lemon Chickpea Bowls",
            ["description"] = null,
            ["servings"]    = 2,
            ["ingredients"] = new JsonArray(ingredients
                .Select(i => (JsonNode)new JsonObject
                {
                    ["name"]         = i.Name,
                    ["display_name"] = i.Name,
                    ["amount"]       = i.Amount,
                    ["unit"]         = i.Unit,
                    ["notes"]        = null,
                })
                .ToArray()),
            ["steps"] = new JsonArray(stepNumbers
                .Select(n => (JsonNode)new JsonObject
                {
                    ["step_number"]        = n,
                    ["instruction"]        = $"Step {n}.",
                    ["ingredient_indexes"] = new JsonArray(),
                    ["stated_amounts"]     = new JsonArray(stepAmounts
                        .Where(a => a.Step == n)
                        .Select(a => (JsonNode)new JsonObject
                        {
                            ["ingredient_index"] = a.Index,
                            ["amount"]           = a.Amount,
                            ["unit"]             = a.Unit,
                        })
                        .ToArray()),
                })
                .ToArray()),
        };
    }

    private static readonly (string, double?, string?)[] ChickpeasOilAndSalt =
        [("chickpeas", 1, "can"), ("olive oil", null, null), ("salt", null, null)];

    private string PromptSent => _llm.Calls[0].UserContent;

    // ── Site Readers ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AMealimePage_PutsEachStepsLinesUnderThatStepInThePrompt()
    {
        var svc = BuildService(Extraction(ChickpeasOilAndSalt), new MealimeSiteReader());

        await svc.ScrapeAsync(MealimePageFixture.Url);

        var step2 = PromptSent.IndexOf("2. Toss the chickpeas", StringComparison.Ordinal);
        var step3 = PromptSent.IndexOf("3. Season with more salt", StringComparison.Ordinal);
        step2.Should().BePositive();
        PromptSent.IndexOf("1 tbsp olive oil", StringComparison.Ordinal).Should().BeInRange(step2, step3);
        PromptSent.LastIndexOf("½ tsp salt", StringComparison.Ordinal).Should().BeGreaterThan(step3);
    }

    [Fact]
    public async Task APageOnAHostNoReaderHandles_SendsOnlyItsJsonLd()
    {
        var svc = BuildService(Extraction(ChickpeasOilAndSalt), new MealimeSiteReader());

        await svc.ScrapeAsync("https://example.com/lemon-chickpea-bowls");

        PromptSent.Should().NotContain("1 tbsp olive oil");
        PromptSent.Should().NotContain(RecipeJsonLdExtractor.StepLinesHeading);
        Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task AReaderThatThrows_IsLoggedAndTheScrapeGoesOnWithoutIt()
    {
        var svc = BuildService(Extraction(ChickpeasOilAndSalt), new BrokenSiteReader(throws: true));

        var preview = await svc.ScrapeAsync(MealimePageFixture.Url);

        preview.Ingredients.Should().HaveCount(3);
        PromptSent.Should().NotContain(RecipeJsonLdExtractor.StepLinesHeading);
        Warnings.Should().ContainSingle(w => w.Contains(nameof(BrokenSiteReader)) && w.Contains(MealimePageFixture.Url));
    }

    [Fact]
    public async Task AReaderThatFindsUnexpectedData_IsLoggedAndTheScrapeGoesOnWithoutIt()
    {
        var svc = BuildService(Extraction(ChickpeasOilAndSalt), new BrokenSiteReader(throws: false));

        var preview = await svc.ScrapeAsync(MealimePageFixture.Url);

        preview.Ingredients.Should().HaveCount(3);
        Warnings.Should().ContainSingle(w => w.Contains(nameof(BrokenSiteReader)) && w.Contains(MealimePageFixture.Url));
    }

    // ── Schema ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheScrapeAsksEachStepForTheAmountsItStates_WhileTheSharedSchemaDoesNot()
    {
        var svc = BuildService(Extraction(ChickpeasOilAndSalt));

        await svc.ScrapeAsync(MealimePageFixture.Url);

        var steps = _llm.Calls[0].Schema["properties"]!["steps"]!["items"]!;
        steps["required"]!.AsArray().Select(r => r!.GetValue<string>()).Should().Contain("stated_amounts");
        steps["properties"]!["stated_amounts"]!["items"]!["required"]!.AsArray()
            .Select(r => r!.GetValue<string>()).Should().BeEquivalentTo(["ingredient_index", "amount", "unit"]);
        RecipeScrapeService.RecipeSchemaJson.Should().NotContain("stated_amounts");
    }

    // ── Filling empty rows ────────────────────────────────────────────────────

    [Fact]
    public async Task AnEmptyRow_IsFilledWithTheTotalOfItsStepAmounts_InTheSharedUnit()
    {
        var svc = BuildService(Extraction(ChickpeasOilAndSalt, (2, 2, 0.5, "tsp"), (3, 2, 0.5, "tsp")));

        var preview = await svc.ScrapeAsync(MealimePageFixture.Url);

        var salt = preview.Ingredients[2];
        (salt.Amount, salt.Unit, salt.SourceAmount, salt.SourceUnit).Should().Be((1m, "tsp", 1m, "tsp"));
        salt.AmountFromSteps.Should().BeTrue();
        salt.StepAmounts.Should().Equal(
            new ScrapePreviewStepAmountResponse(0.5m, "tsp"), new ScrapePreviewStepAmountResponse(0.5m, "tsp"));
    }

    [Fact]
    public async Task ARowWhoseListLineStatesAnAmount_KeepsIt_EvenWhenTheStepsDisagree()
    {
        var svc = BuildService(Extraction(ChickpeasOilAndSalt, (2, 0, 2, "can")));

        var preview = await svc.ScrapeAsync(MealimePageFixture.Url);

        var chickpeas = preview.Ingredients[0];
        (chickpeas.Amount, chickpeas.Unit).Should().Be((1m, "can"));
        chickpeas.AmountFromSteps.Should().BeFalse();
        chickpeas.StepAmounts.Should().BeEmpty();
    }

    [Fact]
    public async Task ARowWhoseListLineStatesASizelessUnit_KeepsIt()
    {
        var svc = BuildService(Extraction([("salt", null, "pinch")], (2, 0, 0.5, "tsp")));

        var preview = await svc.ScrapeAsync(MealimePageFixture.Url);

        var salt = preview.Ingredients[0];
        (salt.Amount, salt.SourceAmount, salt.SourceUnit).Should().Be(((decimal?)null, (decimal?)null, "pinch"));
        salt.AmountFromSteps.Should().BeFalse();
    }

    [Fact]
    public async Task AFilledCup_ResolvesAgainstTheMatchedIngredientsDensity()
    {
        await SeedIngredientAsync("flour", gramsPerMillilitre: 0.5m);
        var svc = BuildService(Extraction([("flour", null, null)], (2, 0, 1, "cup")));

        var preview = await svc.ScrapeAsync(MealimePageFixture.Url);

        var flour = preview.Ingredients[0];
        (flour.Amount, flour.Unit, flour.SourceAmount, flour.SourceUnit).Should().Be((120m, "g", 1m, "cup"));
    }

    [Fact]
    public async Task AVolumeAndAMass_AddUpThroughTheMatchedIngredientsDensity_WithNoSourceMeasurement()
    {
        await SeedIngredientAsync("flour", gramsPerMillilitre: 0.5m);
        var svc = BuildService(Extraction([("flour", null, null)], (2, 0, 100, "g"), (3, 0, 1, "cup")));

        var preview = await svc.ScrapeAsync(MealimePageFixture.Url);

        var flour = preview.Ingredients[0];
        (flour.Amount, flour.Unit, flour.SourceAmount, flour.SourceUnit).Should().Be((220m, "g", (decimal?)null, (string?)null));
        flour.AmountFromSteps.Should().BeTrue();
    }

    [Fact]
    public async Task AVolumeAndAMassWithNoDensity_LeaveTheRowEmpty_ListingTheAmountsFound()
    {
        var svc = BuildService(Extraction(ChickpeasOilAndSalt, (2, 1, 1, "tbsp"), (3, 1, 10, "g")));

        var preview = await svc.ScrapeAsync(MealimePageFixture.Url);

        var oil = preview.Ingredients[1];
        (oil.Amount, oil.Unit).Should().Be(((decimal?)null, (string?)null));
        oil.AmountFromSteps.Should().BeFalse();
        oil.StepAmounts.Should().Equal(
            new ScrapePreviewStepAmountResponse(1m, "tbsp"), new ScrapePreviewStepAmountResponse(10m, "g"));
    }

    [Fact]
    public async Task AnEntryOnlyUnitOnEveryMention_IsConverted_KeepingTheTotalAsTheSource()
    {
        var svc = BuildService(Extraction([("whole milk", null, null)], (2, 0, 2, "fl oz"), (3, 0, 2, "fl oz")));

        var preview = await svc.ScrapeAsync(MealimePageFixture.Url);

        var milk = preview.Ingredients[0];
        (milk.Amount, milk.Unit, milk.SourceAmount, milk.SourceUnit).Should().Be((118.294m, "ml", 4m, "fl oz"));
        milk.EntryUnit.Should().Be("fl oz");
    }

    [Fact]
    public async Task OnlySizelessMentions_LeaveTheRowUnquantified_WithTheWordAsItsSource()
    {
        var svc = BuildService(Extraction(ChickpeasOilAndSalt, (2, 2, 1, "pinch")));

        var preview = await svc.ScrapeAsync(MealimePageFixture.Url);

        var salt = preview.Ingredients[2];
        (salt.Amount, salt.SourceAmount, salt.SourceUnit).Should().Be(((decimal?)null, 1m, "pinch"));
        salt.AmountFromSteps.Should().BeTrue();
    }

    [Fact]
    public async Task AStepAmountForAnIngredientIndexOutOfRange_IsDroppedWithAWarning()
    {
        var svc = BuildService(Extraction(ChickpeasOilAndSalt, (2, 3, 1, "tsp"), (2, 2, 1, "tsp")));

        var preview = await svc.ScrapeAsync(MealimePageFixture.Url);

        preview.Ingredients[2].Amount.Should().Be(1m);
        Warnings.Should().ContainSingle(w => w.Contains("step 2") && w.Contains("3"));
    }

    [Fact]
    public async Task TheStepTextIsTheModelsInstruction_WithNoReaderLinesAdded()
    {
        var svc = BuildService(Extraction(ChickpeasOilAndSalt, (2, 2, 0.5, "tsp")), new MealimeSiteReader());

        var preview = await svc.ScrapeAsync(MealimePageFixture.Url);

        preview.Steps.Select(s => s.Instruction).Should().Equal("Step 1.", "Step 2.");
    }

    private async Task SeedIngredientAsync(string name, decimal? gramsPerMillilitre)
    {
        await using var ctx = db.CreateDbContext();
        ctx.Ingredients.Add(TestDataBuilder.Ingredient(name, name, IngredientCategory.BakingSpices,
            gramsPerMillilitre: gramsPerMillilitre));
        await ctx.SaveChangesAsync();
    }

    private IEnumerable<string> Warnings =>
        _logger.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message);

    private sealed class BrokenSiteReader(bool throws) : ISiteReader
    {
        public IReadOnlyCollection<string> Hosts { get; } = ["www.mealime.com"];

        public IReadOnlyDictionary<int, IReadOnlyList<string>>? ReadStepLines(IDocument page) =>
            throws ? throw new InvalidOperationException("page changed") : null;
    }
}
