using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RecipeApp.API.DTOs.Scrape;
using RecipeApp.API.Services;
using RecipeApp.Tests.Infrastructure;
using RecipeApp.Tests.Services;
using RecipeApp.Tests.Services.Llm;

namespace RecipeApp.Tests.Endpoints;

/// <summary>Scraping with <c>Llm:Provider = Anthropic</c> and no Model Server, through the real Claude client and probe against a scripted Claude API.</summary>
[Collection("Database")]
public class RecipeScrapeAnthropicTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>A page with enough visible text that the scrape never reaches for the headless renderer.</summary>
    private const string RecipePage = """
        <html><body><main>
        <h1>Tomato Soup</h1>
        <p>A simple tomato soup for a cold evening. It takes about thirty minutes from start to finish and
        uses ingredients most kitchens already have, which makes it a dependable weeknight dinner.</p>
        <h2>Ingredients</h2>
        <ul><li>2 tablespoons olive oil</li><li>1 onion, diced</li><li>800 g canned tomatoes</li></ul>
        <h2>Method</h2>
        <ol><li>Soften the onion in the oil over a medium heat for eight minutes.</li>
        <li>Add the tomatoes, bring to the boil and simmer for twenty minutes, then blend until smooth.</li></ol>
        </main></body></html>
        """;

    private static readonly string Extraction = new JsonObject
    {
        ["name"]        = "Tomato Soup",
        ["description"] = null,
        ["servings"]    = 4,
        ["ingredients"] = new JsonArray(new JsonObject
        {
            ["name"] = "onion", ["display_name"] = "Onion", ["amount"] = 1, ["unit"] = null, ["notes"] = "diced",
        }),
        ["steps"] = new JsonArray(new JsonObject
        {
            ["step_number"] = 1, ["instruction"] = "Soften the onion.",
            ["ingredient_indexes"] = new JsonArray(0), ["stated_amounts"] = new JsonArray(),
        }),
    }.ToJsonString();

    /// <summary>Plays the Claude API and the recipe site, recording every URL asked for.</summary>
    private static StubHttpClientFactory Http(Func<HttpResponseMessage>? messages = null) => new(request =>
        request.RequestUri!.AbsolutePath switch
        {
            var path when path.StartsWith("/v1/models/") => AnthropicSse.Model("claude-opus-5-5"),
            "/v1/messages" => messages?.Invoke() ?? AnthropicSse.Message(Extraction),
            _              => StubHttpClientFactory.Ok(RecipePage),
        });

    private HttpClient CreateClient(StubHttpClientFactory http, string apiKey = "sk-ant-test")
    {
        var factory = db.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Provider"]              = "Anthropic",
                ["Llm:Anthropic:ApiKey"]      = apiKey,
                ["Llm:Anthropic:MaxRetries"]  = "0",
                ["Llm:ModelServer:BaseUrl"]   = "",
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(http);
                services.RemoveAll<IHeadlessRenderer>();
                services.AddSingleton<IHeadlessRenderer>(new FakeHeadlessRenderer());
            });
        });
        return factory.CreateClient();
    }

    private static int MessagesCalls(StubHttpClientFactory http) =>
        http.RequestedUrls.Count(url => url.EndsWith("/v1/messages"));

    // ── Working Scrapes ──────────────────────────────────────────────────────

    [Fact]
    public async Task ScrapeUrl_WorksWithNoModelServer()
    {
        var http = Http();

        var response = await CreateClient(http).PostAsJsonAsync("/api/v1/recipes/scrape",
            new ScrapeRecipeRequest("https://example.com/tomato-soup"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ScrapePreviewResponse>())!.Name.Should().Be("Tomato Soup");
        MessagesCalls(http).Should().Be(1);
    }

    [Fact]
    public async Task ScrapeText_WorksWithNoModelServer()
    {
        var response = await CreateClient(Http()).PostAsJsonAsync("/api/v1/recipes/scrape/text",
            new ScrapeTextRequest("Tomato Soup\n\n1 onion, diced\n\n1. Soften the onion."));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ScrapePreviewResponse>())!.Name.Should().Be("Tomato Soup");
    }

    [Fact]
    public async Task Status_ReportsClaudeReady_WithTheConfiguredModel()
    {
        var body = await CreateClient(Http()).GetFromJsonAsync<JsonNode>("/api/v1/llm/status");

        body!["provider"]!.GetValue<string>().Should().Be("anthropic");
        body["state"]!.GetValue<string>().Should().Be("ready");
        body["model"]!.GetValue<string>().Should().Be("claude-opus-5-5");
    }

    // ── Failures ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoApiKey_Returns503SayingClaudeIsNotConfigured_WithoutFetchingOrAskingClaude()
    {
        var http = Http();

        var response = await CreateClient(http, apiKey: "").PostAsJsonAsync("/api/v1/recipes/scrape",
            new ScrapeRecipeRequest("https://example.com/tomato-soup"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail
            .Should().Be("Scraping is unavailable: Claude (Anthropic) isn't configured.");
        http.RequestedUrls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized,        "Scraping is unavailable: Claude (Anthropic) rejected the API key.")]
    [InlineData(HttpStatusCode.Forbidden,           "Scraping is unavailable: Claude (Anthropic) rejected the API key.")]
    [InlineData(HttpStatusCode.TooManyRequests,     "Scraping is unavailable: Claude is busy right now, try again in a moment.")]
    [InlineData((HttpStatusCode)529,                "Scraping is unavailable: Claude is busy right now, try again in a moment.")]
    [InlineData(HttpStatusCode.InternalServerError, "Scraping is unavailable: Claude (Anthropic) can't be reached.")]
    public async Task ClaudeUnavailableDuringExtraction_Returns503_NotAFailedRecipe(HttpStatusCode status, string message)
    {
        var http = Http(() => AnthropicSse.Error(status));

        var response = await CreateClient(http).PostAsJsonAsync("/api/v1/recipes/scrape/text",
            new ScrapeTextRequest("Tomato Soup\n\n1 onion, diced\n\n1. Soften the onion."));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Be(message);
    }

    [Theory]
    [InlineData("refusal")]
    [InlineData("max_tokens")]
    public async Task ClaudeStoppingBeforeTheAnswerIsFinished_Returns422(string stopReason)
    {
        var http = Http(() => AnthropicSse.Message("""{"name":""", stopReason));

        var response = await CreateClient(http).PostAsJsonAsync("/api/v1/recipes/scrape/text",
            new ScrapeTextRequest("Tomato Soup\n\n1 onion, diced\n\n1. Soften the onion."));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Claude's own budget is shorter than Scraping's, so running it out must still read as a timeout.</summary>
    [Fact]
    public async Task ClaudeNotAnsweringWithinItsTimeout_Returns422SayingExtractionTimedOut()
    {
        var http = new StubHttpClientFactory(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.StartsWith("/v1/models/")) return AnthropicSse.Model("claude-opus-5-5");
            await Task.Delay(Timeout.Infinite, ct);
            return AnthropicSse.Message(Extraction);
        });
        var client = db.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Provider"]                 = "Anthropic",
                ["Llm:Anthropic:ApiKey"]         = "sk-ant-test",
                ["Llm:Anthropic:MaxRetries"]     = "0",
                ["Llm:Anthropic:TimeoutSeconds"] = "1",
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(http);
            });
        }).CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/recipes/scrape/text",
            new ScrapeTextRequest("Tomato Soup\n\n1 onion, diced\n\n1. Soften the onion."));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail
            .Should().Be("Recipe extraction timed out. Please try again.");
    }

    /// <summary>A 400 is a bug in what the app sent, so it surfaces as a server error rather than blaming the recipe.</summary>
    [Fact]
    public async Task ClaudeRejectingTheRequest_Returns500()
    {
        var http = Http(() => AnthropicSse.Error(HttpStatusCode.BadRequest, "invalid_request_error"));

        var response = await CreateClient(http).PostAsJsonAsync("/api/v1/recipes/scrape/text",
            new ScrapeTextRequest("Tomato Soup\n\n1 onion, diced\n\n1. Soften the onion."));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }
}
