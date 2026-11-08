using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecipeApp.API.Services;
using RecipeApp.API.Services.Llm;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Services;

/// <summary>How a scrape treats the step-to-ingredient links the model returns, which it can number past the end of the ingredient list.</summary>
[Collection("Database")]
public class RecipeScrapeServiceStepIndexTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private const string Url ="https://example.com/salsa-chicken";

    /// <summary>A page with enough visible text that the scrape never reaches for the headless renderer.</summary>
    private const string RecipePage = """
        <html><body><main>
        <h1>Salsa Chicken</h1>
        <p>Grilled chicken with a quick tomato salsa. It takes about thirty minutes from start to finish and
        uses ingredients most kitchens already have, which makes it a dependable weeknight dinner.</p>
        <ul><li>2 chicken breasts</li><li>1 tomato</li><li>1 tbsp honey</li></ul>
        <ol><li>Mix the honey and spices.</li><li>Grill the chicken.</li><li>Chop the tomato.</li></ol>
        </main></body></html>
        """;

    private readonly RecordingLogger<RecipeScrapeService> _logger = new();

    /// <summary>A scrape whose model reads three ingredients and links the steps to <paramref name="stepIndexes"/>.</summary>
    private RecipeScrapeService BuildService(params int[][] stepIndexes)
    {
        var steps = new JsonArray(stepIndexes
            .Select((indexes, i) => (JsonNode)new JsonObject
            {
                ["step_number"]        = i + 1,
                ["instruction"]        = $"Step {i + 1}.",
                ["ingredient_indexes"] = new JsonArray(indexes.Select(x => (JsonNode)x).ToArray()),
            })
            .ToArray());

        var extraction = new JsonObject
        {
            ["name"]        = "Salsa Chicken",
            ["description"] = null,
            ["servings"]    = 4,
            ["ingredients"] = new JsonArray(
                Ingredient("chicken breast", "Chicken Breast", 2, "pcs"),
                Ingredient("tomato", "Tomato", 1, "pcs"),
                Ingredient("honey", "Honey", 1, "tbsp")),
            ["steps"] = steps,
        };

        return new RecipeScrapeService(
            httpClientFactory:    StubHttpClientFactory.Returning(RecipePage),
            options:              Options.Create(new RecipeScrapingOptions()),
            llm:                  new StubLlmStructuredClient(extraction),
            modelServer:          new StubModelServerStatusProbe(ModelServerState.Ready, "model.gguf"),
            db:                   db.CreateDbContext(),
            headlessRenderer:     new FakeHeadlessRenderer(),
            measurementConverter: new MeasurementConverter(Options.Create(new MeasurementOptions())),
            logger:               _logger);
    }

    private static JsonObject Ingredient(string name, string display, double amount, string unit) => new()
    {
        ["name"]         = name,
        ["display_name"] = display,
        ["amount"]       = amount,
        ["unit"]         = unit,
        ["notes"]        = null,
    };

    private IEnumerable<string> Warnings =>
        _logger.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Scrape_DropsStepIndexesPastEitherEndOfTheIngredients_KeepingTheRestInOrder()
    {
        var svc = BuildService([2, 3, 0, -1, 4], [1], [0, 2]);

        var preview = await svc.ScrapeAsync(Url);

        preview.Steps.Select(s => s.IngredientIndexes).Should().BeEquivalentTo(
            new List<int>[] { [2, 0], [1], [0, 2] },
            o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Scrape_DropsARepeatedStepIndex()
    {
        var svc = BuildService([1, 0, 1]);

        var preview = await svc.ScrapeAsync(Url);

        preview.Steps.Single().IngredientIndexes.Should().Equal(1, 0);
    }

    [Fact]
    public async Task Scrape_WarnsOncePerStepWithDroppedIndexes_NamingTheUrlStepAndIndexes()
    {
        var svc = BuildService([0, 3, 4], [1], [-1]);

        await svc.ScrapeAsync(Url);

        Warnings.Should().HaveCount(2);
        Warnings.Should().ContainSingle(w => w.Contains(Url) && w.Contains("step 1") && w.Contains("3, 4"));
        Warnings.Should().ContainSingle(w => w.Contains(Url) && w.Contains("step 3") && w.Contains("-1"));
    }

    [Fact]
    public async Task Scrape_WithEveryIndexInRange_LogsNoWarning()
    {
        var svc = BuildService([0, 1, 2]);

        await svc.ScrapeAsync(Url);

        Warnings.Should().BeEmpty();
    }
}
