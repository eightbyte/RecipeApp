using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RecipeApp.API.DTOs.Scrape;
using RecipeApp.API.Services;
using RecipeApp.API.Services.Llm;
using RecipeApp.Tests.Infrastructure;
using RecipeApp.Tests.Services;

namespace RecipeApp.Tests.Endpoints;

/// <summary>How the API behaves around the Model Server's availability: the status it reports, Scraping that fails fast, and readiness that never waits on it.</summary>
[Collection("Database")]
public class ModelServerEndpointsTests(DatabaseFixture db) : IAsyncLifetime
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
        <ul><li>2 tablespoons olive oil</li><li>1 onion, diced</li><li>2 cloves garlic</li>
        <li>800 g canned tomatoes</li><li>500 ml vegetable stock</li><li>Salt and pepper to taste</li></ul>
        <h2>Method</h2>
        <ol><li>Soften the onion and garlic in the oil over a medium heat for eight minutes.</li>
        <li>Add the tomatoes and stock, bring to the boil and simmer for twenty minutes.</li>
        <li>Blend until smooth and season to taste before serving.</li></ol>
        </main></body></html>
        """;

    private HttpClient CreateClient(
        IModelServerStatusProbe probe,
        StubHttpClientFactory? pages = null,
        ILlmStructuredClient? llm = null)
    {
        var factory = db.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IModelServerStatusProbe>();
            services.AddSingleton(probe);

            services.RemoveAll<IHeadlessRenderer>();
            services.AddSingleton<IHeadlessRenderer>(new FakeHeadlessRenderer());

            if (pages is not null)
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(pages);
            }

            if (llm is not null)
            {
                services.RemoveAll<ILlmStructuredClient>();
                services.AddSingleton(llm);
            }
        }));

        return factory.CreateClient();
    }

    // ── GET /model-server/status ─────────────────────────────────────────────

    [Fact]
    public async Task Status_Ready_ReportsTheStateAndTheLoadedModel()
    {
        var client = CreateClient(new StubModelServerStatusProbe(ModelServerState.Ready, "Qwen3.5-9B-Q6_K.gguf"));

        var body = await client.GetFromJsonAsync<JsonNode>("/api/v1/model-server/status");

        body!["state"]!.GetValue<string>().Should().Be("ready");
        body["model"]!.GetValue<string>().Should().Be("Qwen3.5-9B-Q6_K.gguf");
    }

    [Theory]
    [InlineData(ModelServerState.Offline,      "offline")]
    [InlineData(ModelServerState.Loading,      "loading")]
    [InlineData(ModelServerState.Unauthorized, "unauthorized")]
    public async Task Status_NotReady_ReportsTheStateWithNoModel(ModelServerState state, string wire)
    {
        var client = CreateClient(new StubModelServerStatusProbe(state));

        var body = await client.GetFromJsonAsync<JsonNode>("/api/v1/model-server/status");

        body!["state"]!.GetValue<string>().Should().Be(wire);
        body["model"].Should().BeNull();
    }

    // ── POST /recipes/scrape ─────────────────────────────────────────────────

    [Theory]
    [InlineData(ModelServerState.Offline,      "Scraping is unavailable: the Model Server isn't running.")]
    [InlineData(ModelServerState.Loading,      "Scraping is unavailable: the Model Server is still loading the model, try again in a moment.")]
    [InlineData(ModelServerState.Unauthorized, "Scraping is unavailable: the Model Server rejected the API key.")]
    public async Task Scrape_ModelServerNotReady_Returns503NamingTheState_WithoutFetchingThePage(
        ModelServerState state, string message)
    {
        var pages  = StubHttpClientFactory.Returning(RecipePage);
        var llm    = new StubLlmStructuredClient(new JsonObject());
        var client = CreateClient(new StubModelServerStatusProbe(state), pages, llm);

        var response = await client.PostAsJsonAsync("/api/v1/recipes/scrape",
            new ScrapeRecipeRequest("https://example.com/tomato-soup"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Be(message);
        pages.RequestedUrls.Should().BeEmpty("a scrape that cannot be extracted must not spend the fetch");
        llm.CallCount.Should().Be(0);
    }

    /// <summary>The pre-check can pass and the server still stop, restart with a new key or reload before extraction runs.</summary>
    [Fact]
    public async Task Scrape_ModelServerGoesAwayDuringExtraction_Returns503_NotTheExtractionFailed422()
    {
        var pages  = StubHttpClientFactory.Returning(RecipePage);
        var llm    = new StubLlmStructuredClient(_ => throw new ModelServerUnavailableException(ModelServerState.Offline));
        var client = CreateClient(new StubModelServerStatusProbe(ModelServerState.Ready, "model.gguf"), pages, llm);

        var response = await client.PostAsJsonAsync("/api/v1/recipes/scrape",
            new ScrapeRecipeRequest("https://example.com/tomato-soup"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail
            .Should().Be("Scraping is unavailable: the Model Server isn't running.");
        pages.RequestedUrls.Should().ContainSingle();
    }

    // ── /health/ready ────────────────────────────────────────────────────────

    /// <summary>The updater rolls back on a failed readiness check, so a stopped Model Server must never fail a deploy.</summary>
    [Fact]
    public async Task Readiness_IsHealthy_WithTheModelServerStopped()
    {
        var probe  = new StubModelServerStatusProbe(ModelServerState.Offline);
        var client = CreateClient(probe);

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        probe.CallCount.Should().Be(0);
    }
}
