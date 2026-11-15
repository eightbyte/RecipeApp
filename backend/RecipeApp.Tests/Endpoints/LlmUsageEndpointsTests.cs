using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using RecipeApp.API.Models;
using RecipeApp.API.Services.Llm;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Endpoints;

/// <summary>GET /llm/usage: the LLM Calls in a range, newest first and paged, with totals per provider, model and Operation.</summary>
[Collection("Database")]
public class LlmUsageEndpointsTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static readonly DateTime Now = DateTime.UtcNow;

    private static LlmCall Call(
        DateTime createdAt,
        LlmProvider provider   = LlmProvider.Anthropic,
        string? model          = "claude-opus-5-5",
        LlmOperation operation = LlmOperation.ScrapeUrl,
        int? input = 100, int? cacheRead = 10, int? cacheWrite = 1, int? output = 50,
        decimal? cost = 0.01m) => new()
    {
        Id               = Guid.NewGuid(),
        CreatedAt        = createdAt,
        Provider         = provider,
        Model            = model,
        Operation        = operation,
        Step             = LlmStep.Extraction,
        CorrelationId    = Guid.NewGuid(),
        SourceUrl        = "https://example.test/soup",
        Outcome          = LlmCallOutcome.Succeeded,
        DurationMs       = 900,
        InputTokens      = input,
        CacheReadTokens  = cacheRead,
        CacheWriteTokens = cacheWrite,
        OutputTokens     = output,
        EstimatedCostUsd = cost,
    };

    private async Task SeedAsync(params LlmCall[] calls)
    {
        await using var ctx = db.CreateDbContext();
        ctx.LlmCalls.AddRange(calls);
        await ctx.SaveChangesAsync();
    }

    private async Task<JsonNode> GetAsync(string query = "")
    {
        var response = await db.CreateClient().GetAsync($"/api/v1/llm/usage{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    private static List<Guid> ItemIds(JsonNode body) =>
        body["items"]!.AsArray().Select(item => item!["id"]!.GetValue<Guid>()).ToList();

    // ── Range and order ──────────────────────────────────────────────────────

    [Fact]
    public async Task ByDefault_ListsTheLast30Days_NewestFirst()
    {
        var older  = Call(Now.AddDays(-2));
        var newest = Call(Now.AddMinutes(-5));
        var stale  = Call(Now.AddDays(-31));
        await SeedAsync(older, newest, stale);

        var body = await GetAsync();

        ItemIds(body).Should().Equal(newest.Id, older.Id);
        body["totalCount"]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public async Task ByDefault_EndsAtNow()
    {
        var past   = Call(Now.AddHours(-1));
        var future = Call(Now.AddDays(1));
        await SeedAsync(past, future);

        ItemIds(await GetAsync()).Should().Equal(past.Id);
    }

    /// <summary>Half-open, so consecutive days never count a call twice.</summary>
    [Fact]
    public async Task FromIsInclusive_AndToExclusive()
    {
        var atFrom = Call(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        var inside = Call(new DateTime(2026, 11, 1, 23, 59, 0, DateTimeKind.Utc));
        var atTo   = Call(new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc));
        await SeedAsync(atFrom, inside, atTo);

        var body = await GetAsync("?from=2026-11-01&to=2026-11-02");

        ItemIds(body).Should().BeEquivalentTo([atFrom.Id, inside.Id]);
    }

    [Fact]
    public async Task FiltersByOperationAndProvider()
    {
        var wanted       = Call(Now.AddHours(-1), LlmProvider.Anthropic, operation: LlmOperation.ScrapeText);
        var otherOp      = Call(Now.AddHours(-2), LlmProvider.Anthropic, operation: LlmOperation.ScrapeUrl);
        var otherServer  = Call(Now.AddHours(-3), LlmProvider.ModelServer, "q.gguf", LlmOperation.ScrapeText, cost: 0m);
        await SeedAsync(wanted, otherOp, otherServer);

        var body = await GetAsync("?operation=ScrapeText&provider=anthropic");

        ItemIds(body).Should().Equal(wanted.Id);
    }

    [Fact]
    public async Task PagesTheItems_ButTotalsTheWholeRange()
    {
        var calls = Enumerable.Range(1, 5).Select(hour => Call(Now.AddHours(-hour))).ToArray();
        await SeedAsync(calls);

        var body = await GetAsync("?page=2&pageSize=2");

        ItemIds(body).Should().Equal(calls[2].Id, calls[3].Id);
        body["totalCount"]!.GetValue<int>().Should().Be(5);
        body["totals"]![0]!["calls"]!.GetValue<int>().Should().Be(5);
    }

    // ── Items ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnItem_CarriesItsCorrelationIdAndEveryRecordedField()
    {
        var call = Call(Now.AddMinutes(-1));
        await SeedAsync(call);

        var item = (await GetAsync())["items"]![0]!;

        item["correlationId"]!.GetValue<Guid>().Should().Be(call.CorrelationId);
        item["provider"]!.GetValue<string>().Should().Be("anthropic");
        item["model"]!.GetValue<string>().Should().Be("claude-opus-5-5");
        item["operation"]!.GetValue<string>().Should().Be("ScrapeUrl");
        item["step"]!.GetValue<string>().Should().Be("Extraction");
        item["outcome"]!.GetValue<string>().Should().Be("Succeeded");
        item["sourceUrl"]!.GetValue<string>().Should().Be("https://example.test/soup");
        item["durationMs"]!.GetValue<int>().Should().Be(900);
        item["inputTokens"]!.GetValue<int>().Should().Be(100);
        item["cacheReadTokens"]!.GetValue<int>().Should().Be(10);
        item["cacheWriteTokens"]!.GetValue<int>().Should().Be(1);
        item["outputTokens"]!.GetValue<int>().Should().Be(50);
        item["estimatedCostUsd"]!.GetValue<decimal>().Should().Be(0.01m);
    }

    // ── Totals ───────────────────────────────────────────────────────────────

    /// <summary>One unpriced call is counted beside the priced sum rather than blanking it.</summary>
    [Fact]
    public async Task Totals_GroupByProviderModelAndOperation_CountingUnpricedCallsApart()
    {
        await SeedAsync(
            Call(Now.AddHours(-1), input: 100, cacheRead: 10, cacheWrite: 1, output: 50, cost: 0.01m),
            Call(Now.AddHours(-2), input: 200, cacheRead: 20, cacheWrite: 2, output: 60, cost: 0.02m),
            Call(Now.AddHours(-3), input: null, cacheRead: null, cacheWrite: null, output: null, cost: null),
            Call(Now.AddHours(-4), LlmProvider.Anthropic, "claude-unpriced", cost: null),
            Call(Now.AddHours(-5), LlmProvider.ModelServer, "q.gguf", LlmOperation.SeedNormalise, cacheWrite: null, cost: 0m));

        var totals = (await GetAsync())["totals"]!.AsArray();

        totals.Should().HaveCount(3);
        var opus = totals.Single(t => t!["model"]!.GetValue<string>() == "claude-opus-5-5")!;
        opus["provider"]!.GetValue<string>().Should().Be("anthropic");
        opus["operation"]!.GetValue<string>().Should().Be("ScrapeUrl");
        opus["calls"]!.GetValue<int>().Should().Be(3);
        opus["inputTokens"]!.GetValue<long>().Should().Be(300);
        opus["cacheReadTokens"]!.GetValue<long>().Should().Be(30);
        opus["cacheWriteTokens"]!.GetValue<long>().Should().Be(3);
        opus["outputTokens"]!.GetValue<long>().Should().Be(110);
        opus["estimatedCostUsd"]!.GetValue<decimal>().Should().Be(0.03m);
        opus["unpricedCalls"]!.GetValue<int>().Should().Be(1);

        var unpriced = totals.Single(t => t!["model"]!.GetValue<string>() == "claude-unpriced")!;
        unpriced["estimatedCostUsd"]!.GetValue<decimal>().Should().Be(0m);
        unpriced["unpricedCalls"]!.GetValue<int>().Should().Be(1);

        var local = totals.Single(t => t!["provider"]!.GetValue<string>() == "modelServer")!;
        local["operation"]!.GetValue<string>().Should().Be("SeedNormalise");
        local["estimatedCostUsd"]!.GetValue<decimal>().Should().Be(0m);
        local["unpricedCalls"]!.GetValue<int>().Should().Be(0);
    }

    // ── Validation ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("?operation=Scraping")]
    [InlineData("?provider=openai")]
    [InlineData("?provider=ModelServer")]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?from=2026-11-02&to=2026-11-01")]
    [InlineData("?from=2026-11-02&to=2026-11-02")]
    public async Task AnInvalidQuery_IsA400(string query)
    {
        var response = await db.CreateClient().GetAsync($"/api/v1/llm/usage{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
