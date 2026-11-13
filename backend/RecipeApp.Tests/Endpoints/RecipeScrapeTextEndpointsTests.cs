using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RecipeApp.API.Validators;
using RecipeApp.API.DTOs.Scrape;
using RecipeApp.API.Services;
using RecipeApp.API.Services.Llm;
using RecipeApp.Tests.Infrastructure;
using RecipeApp.Tests.Services;

namespace RecipeApp.Tests.Endpoints;

/// <summary>A Scrape from pasted text, run through the real service with a scripted model and no network.</summary>
[Collection("Database")]
public class RecipeScrapeTextEndpointsTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private const string Route = "/api/v1/recipes/scrape/text";

    private const string PastedRecipe = """
        Honey Chicken

        INGREDIENTS
        • 2 chicken breasts
        • 1 tbsp honey

        STEPS
        1. Brush the chicken with honey.
        2. Grill until cooked through.
        """;

    private static JsonObject Extraction(bool withIngredients = true, bool withSteps = true) => new()
    {
        ["name"]        = "Honey Chicken",
        ["description"] = null,
        ["servings"]    = 2,
        ["ingredients"] = withIngredients
            ? new JsonArray(Ingredient("chicken breast", "Chicken Breast", 2, null), Ingredient("honey", "Honey", 1, "tbsp"))
            : new JsonArray(),
        ["steps"] = withSteps
            ? new JsonArray(Step(1, "Brush the chicken with honey.", 0, 1), Step(2, "Grill until cooked through.", 0))
            : new JsonArray(),
    };

    private static JsonObject Ingredient(string name, string display, double amount, string? unit) => new()
    {
        ["name"]         = name,
        ["display_name"] = display,
        ["amount"]       = amount,
        ["unit"]         = unit,
        ["notes"]        = null,
    };

    private static JsonObject Step(int number, string instruction, params int[] indexes) => new()
    {
        ["step_number"]        = number,
        ["instruction"]        = instruction,
        ["ingredient_indexes"] = new JsonArray(indexes.Select(i => (JsonNode)i).ToArray()),
        ["stated_amounts"]     = new JsonArray(),
    };

    private HttpClient CreateClient(
        StubLlmStructuredClient llm, ModelServerState state = ModelServerState.Ready, string page = "")
    {
        var factory = db.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(StubHttpClientFactory.Returning(page));
            services.RemoveAll<IHeadlessRenderer>();
            services.AddSingleton<IHeadlessRenderer>(new FakeHeadlessRenderer());
            services.RemoveAll<IModelServerStatusProbe>();
            services.AddSingleton<IModelServerStatusProbe>(new StubModelServerStatusProbe(state, "model.gguf"));
            services.RemoveAll<ILlmStructuredClient>();
            services.AddSingleton<ILlmStructuredClient>(llm);
        }));
        return factory.CreateClient();
    }

    private static Task<HttpResponseMessage> PostText(HttpClient client, string text) =>
        client.PostAsJsonAsync(Route, new ScrapeTextRequest(text));

    // ── A draft ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScrapeText_ReturnsTheDraftTheModelExtracted_WithNoPhoto()
    {
        var response = await PostText(CreateClient(new StubLlmStructuredClient(Extraction())), PastedRecipe);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = await response.Content.ReadFromJsonAsync<ScrapePreviewResponse>();
        preview!.Name.Should().Be("Honey Chicken");
        preview.Servings.Should().Be(2);
        preview.Ingredients.Select(i => i.DisplayName).Should().Equal("Chicken Breast", "Honey");
        preview.Steps.Select(s => s.Instruction).Should().Equal("Brush the chicken with honey.", "Grill until cooked through.");
        preview.ImageUrl.Should().BeNull();
    }

    [Theory]
    [InlineData(ModelServerState.Offline)]
    [InlineData(ModelServerState.Loading)]
    [InlineData(ModelServerState.Unauthorized)]
    public async Task ScrapeText_ModelServerNotReady_Returns503WithoutAskingTheModel(ModelServerState state)
    {
        var llm = new StubLlmStructuredClient(Extraction());

        var response = await PostText(CreateClient(llm, state), PastedRecipe);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        llm.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ScrapeText_ExtractionWithNoIngredientsAndNoSteps_Returns422SayingTheTextHeldNoRecipe()
    {
        var llm = new StubLlmStructuredClient(Extraction(withIngredients: false, withSteps: false));

        var response = await PostText(CreateClient(llm), "Remember to buy a birthday card for Sam on Friday.");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.Should().Be("Couldn't find a recipe in that text.");
    }

    [Fact]
    public async Task ScrapeText_SendsTheModelTheTextsLineBreaks_CollapsingOnlyRunsOfBlankLines()
    {
        var llm  = new StubLlmStructuredClient(Extraction());
        var text = "\r\n  Honey Chicken\r\n\r\n\r\n\r\n• 2 chicken breasts\r\n• 1 tbsp honey\r\n\r\n\r\nBrush with honey.   Grill.\r\n\r\n";

        await PostText(CreateClient(llm), text);

        var sent = llm.Calls.First().UserContent;
        sent.Should().Contain("Honey Chicken\n\n• 2 chicken breasts\n• 1 tbsp honey\n\nBrush with honey.   Grill.\n---");
        sent.Should().NotContain("\r");
        sent.Should().NotContain("\n\n\n");
    }

    // ── Source URL ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScrapeText_NamingNoUrl_HasNoSourceUrl()
    {
        var preview = await ScrapeTextPreview(PastedRecipe);

        preview.SourceUrl.Should().BeNull();
    }

    [Fact]
    public async Task ScrapeText_NamingExactlyOneUrl_TakesItAsTheSourceUrl()
    {
        var preview = await ScrapeTextPreview(PastedRecipe + "\nSOURCE URL\nhttps://www.mealime.com/recipes/honey-chicken/26620\n");

        preview.SourceUrl.Should().Be("https://www.mealime.com/recipes/honey-chicken/26620");
    }

    [Fact]
    public async Task ScrapeText_NamingOneUrlAtTheEndOfASentence_LeavesOutTheFullStop()
    {
        var preview = await ScrapeTextPreview(PastedRecipe + "\nAdapted from http://example.com/honey-chicken.");

        preview.SourceUrl.Should().Be("http://example.com/honey-chicken");
    }

    [Fact]
    public async Task ScrapeText_NamingTwoUrls_HasNoSourceUrl()
    {
        var preview = await ScrapeTextPreview(
            PastedRecipe + "\nhttps://example.com/honey-chicken\nSee also https://example.com/salsa");

        preview.SourceUrl.Should().BeNull();
    }

    [Fact]
    public async Task ScrapeText_SourceUrlComesFromTheText_NeverTheModel()
    {
        var extraction = Extraction();
        extraction["source_url"] = "https://invented.example.com/recipe";

        var response = await PostText(CreateClient(new StubLlmStructuredClient(extraction)), PastedRecipe);

        var preview = await response.Content.ReadFromJsonAsync<ScrapePreviewResponse>();
        preview!.SourceUrl.Should().BeNull();
    }

    private async Task<ScrapePreviewResponse> ScrapeTextPreview(string text)
    {
        var response = await PostText(CreateClient(new StubLlmStructuredClient(Extraction())), text);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ScrapePreviewResponse>())!;
    }

    // ── The URL Scrape is unchanged ───────────────────────────────────────────

    [Fact]
    public async Task UrlScrape_StillKeepsItsUrlAsTheSourceUrl()
    {
        const string page = """
            <html><body><script type="application/ld+json">
            {"@type":"Recipe","name":"Honey Chicken","recipeIngredient":["2 chicken breasts","1 tbsp honey"],"recipeInstructions":["Brush.","Grill."]}
            </script></body></html>
            """;
        var client = CreateClient(new StubLlmStructuredClient(Extraction()), page: page);

        var response = await client.PostAsJsonAsync("/api/v1/recipes/scrape",
            new ScrapeRecipeRequest("https://example.com/honey-chicken"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ScrapePreviewResponse>())!.SourceUrl
            .Should().Be("https://example.com/honey-chicken");
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n\t  ")]
    [InlineData("  chicken and honey  ")]     // 18 characters once trimmed
    public async Task ScrapeText_BlankOrUnderTwentyCharacters_Returns400WithoutAskingTheModel(string text)
    {
        var llm = new StubLlmStructuredClient(Extraction());

        var response = await PostText(CreateClient(llm), text);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        llm.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ScrapeText_BlankText_ReportsOneReason()
    {
        var response = await PostText(CreateClient(new StubLlmStructuredClient(Extraction())), "   ");

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors[nameof(ScrapeTextRequest.Text)].Should().ContainSingle();
    }

    [Fact]
    public async Task ScrapeText_OverThirtyThousandCharacters_Returns400RatherThanTruncating()
    {
        var llm = new StubLlmStructuredClient(Extraction());

        var response = await PostText(CreateClient(llm), new string('a', 30_001));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey(nameof(ScrapeTextRequest.Text));
        llm.CallCount.Should().Be(0);
    }

    [Fact]
    public void TheModelReadsEveryCharacterThePasteLimitAllows()
    {
        var options = db.Factory.Services.GetRequiredService<IOptions<RecipeScrapingOptions>>().Value;

        options.MaxHtmlCharacters.Should().BeGreaterThanOrEqualTo(ScrapeLimits.TextMaxLength,
            "a paste the validator accepts must never be cut short before the model reads it");
    }

    [Fact]
    public async Task ScrapeText_AtExactlyThirtyThousandCharacters_IsAccepted()
    {
        var text = PastedRecipe + new string('a', 30_000 - PastedRecipe.Length);

        var response = await PostText(CreateClient(new StubLlmStructuredClient(Extraction())), text);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
