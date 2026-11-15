using System.Net;
using RecipeApp.API.Services.Llm;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Services.Llm;

/// <summary>The probe asks the Models API about the configured model, so a status check proves the key and the model without spending tokens.</summary>
public class AnthropicStatusProbeTests
{
    private static AnthropicStatusProbe Probe(StubHttpClientFactory http, string apiKey = "sk-ant-test", int statusTimeoutSeconds = 3) =>
        new(http, Microsoft.Extensions.Options.Options.Create(new LlmOptions
        {
            Provider  = LlmProvider.Anthropic,
            Anthropic = new AnthropicOptions { ApiKey = apiKey, StatusTimeoutSeconds = statusTimeoutSeconds },
        }));

    [Fact]
    public async Task NoApiKey_IsNotConfigured_WithoutAskingClaude()
    {
        var http = new StubHttpClientFactory(_ => AnthropicSse.Model("claude-opus-5-5"));

        var status = await Probe(http, apiKey: "").GetStatusAsync();

        status.Should().Be(new LlmStatus(LlmProvider.Anthropic, LlmState.NotConfigured));
        http.RequestedUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task TheModelsApiAnswering_IsReady_NamingTheConfiguredModel()
    {
        var http = new StubHttpClientFactory(_ => AnthropicSse.Model("claude-opus-5-5"));

        var status = await Probe(http).GetStatusAsync();

        status.Should().Be(new LlmStatus(LlmProvider.Anthropic, LlmState.Ready, "claude-opus-5-5"));
        http.RequestedUrls.Should().ContainSingle()
            .Which.Should().EndWith("/v1/models/claude-opus-5-5");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized,        LlmState.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden,           LlmState.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests,     LlmState.Busy)]
    [InlineData((HttpStatusCode)529,                LlmState.Busy)]
    [InlineData(HttpStatusCode.NotFound,            LlmState.NotConfigured)]
    [InlineData(HttpStatusCode.InternalServerError, LlmState.Offline)]
    public async Task AFailedAnswer_ReportsTheStateItProves(HttpStatusCode status, LlmState expected)
    {
        var http = new StubHttpClientFactory(_ => AnthropicSse.Error(status));

        var result = await Probe(http).GetStatusAsync();

        result.Should().Be(new LlmStatus(LlmProvider.Anthropic, expected));
    }

    /// <summary>A status check must answer quickly, so it never waits out the SDK's retries.</summary>
    [Fact]
    public async Task AFailedAnswer_IsNotRetried()
    {
        var http = new StubHttpClientFactory(_ => AnthropicSse.Error(HttpStatusCode.TooManyRequests));

        await Probe(http).GetStatusAsync();

        http.RequestedUrls.Should().ContainSingle();
    }

    [Fact]
    public async Task NoConnection_IsOffline()
    {
        var http = new StubHttpClientFactory(_ => throw new HttpRequestException("connection refused"));

        var status = await Probe(http).GetStatusAsync();

        status.State.Should().Be(LlmState.Offline);
    }

    [Fact]
    public async Task NoAnswerWithinTheStatusTimeout_IsOffline()
    {
        var http = new StubHttpClientFactory(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return AnthropicSse.Model("claude-opus-5-5");
        });

        var status = await Probe(http, statusTimeoutSeconds: 1).GetStatusAsync();

        status.State.Should().Be(LlmState.Offline);
    }
}
