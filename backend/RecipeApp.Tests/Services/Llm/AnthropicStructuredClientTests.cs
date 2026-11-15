using System.Net;
using System.Text.Json.Nodes;
using RecipeApp.API.Services.Llm;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Services.Llm;

/// <summary>The contract with the Claude API, exercised against a scripted handler so CI needs no key and makes no call.</summary>
public class AnthropicStructuredClientTests
{
    private static readonly JsonNode Schema = JsonNode.Parse("""
        {
          "type": "object",
          "properties": { "name": { "type": "string" }, "note": { "type": ["string", "null"] } },
          "required": ["name", "note"]
        }
        """)!;

    private static LlmOptions Options(string apiKey = "sk-ant-test") => new()
    {
        Provider  = LlmProvider.Anthropic,
        Anthropic = new AnthropicOptions { ApiKey = apiKey, MaxRetries = 0 },
    };

    private static AnthropicStructuredClient Client(StubHttpClientFactory http, LlmOptions? options = null) =>
        new(http, Microsoft.Extensions.Options.Options.Create(options ?? Options()));

    private static async Task<(HttpRequestMessage Request, JsonNode Body, JsonNode Answer)> CaptureAsync(int maxTokens = 4096)
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var http = new StubHttpClientFactory(request =>
        {
            captured = request;
            body     = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return AnthropicSse.Message("""{"name":"Soup","note":null}""");
        });

        var answer = await Client(http).CompleteStructuredAsync("SYSTEM", "USER", Schema, maxTokens);

        return (captured!, JsonNode.Parse(body!)!, answer);
    }

    // ── The request ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Completion_ReturnsTheTextBlockAsJson_SkippingTheThinkingBlock()
    {
        var (_, _, answer) = await CaptureAsync();

        answer["name"]!.GetValue<string>().Should().Be("Soup");
    }

    [Fact]
    public async Task Request_StreamsTheConfiguredModel_WithTheCallersPrompts()
    {
        var (request, body, _) = await CaptureAsync();

        request.RequestUri!.AbsolutePath.Should().Be("/v1/messages");
        request.Headers.Authorization!.ToString().Should().Be("Bearer sk-ant-test");
        body["model"]!.GetValue<string>().Should().Be("claude-opus-5-5");
        body["stream"]!.GetValue<bool>().Should().BeTrue();
        body["system"]!.ToJsonString().Should().Contain("SYSTEM");
        body["messages"]![0]!["role"]!.GetValue<string>().Should().Be("user");
        body["messages"]![0]!["content"]!.ToJsonString().Should().Contain("USER");
    }

    /// <summary>Thinking can't be turned off on the default model and its tokens count against max_tokens, so the caller's limit alone would truncate the answer.</summary>
    [Fact]
    public async Task Request_AddsTheThinkingHeadroomToTheCallersTokenLimit_AndSendsNoThinkingSetting()
    {
        var (_, body, _) = await CaptureAsync(maxTokens: 4096);

        body["max_tokens"]!.GetValue<int>().Should().Be(4096 + 8000);
        body.AsObject().ContainsKey("thinking").Should().BeFalse();
    }

    [Fact]
    public async Task Request_ConstrainsTheAnswerWithStructuredOutputs_AtLowEffort()
    {
        var (_, body, _) = await CaptureAsync();

        var outputConfig = body["output_config"]!;
        outputConfig["effort"]!.GetValue<string>().Should().Be("low");
        outputConfig["format"]!["type"]!.GetValue<string>().Should().Be("json_schema");
        outputConfig["format"]!["schema"]!.ToJsonString().Should().Be(StructuredOutputSchema.Fit(Schema).ToJsonString());
        body.AsObject().ContainsKey("tools").Should().BeFalse();
    }

    /// <summary>Current Claude models answer a sampling field with a 400.</summary>
    [Fact]
    public async Task Request_SendsNoSamplingParameters()
    {
        var (_, body, _) = await CaptureAsync();

        foreach (var field in new[] { "temperature", "top_k", "top_p", "min_p" })
            body.AsObject().ContainsKey(field).Should().BeFalse(field);
    }

    // ── Failures: the error-mapping table ────────────────────────────────────

    private static Func<Task<JsonNode>> Calling(StubHttpClientFactory http, LlmOptions? options = null) =>
        () => Client(http, options).CompleteStructuredAsync("SYSTEM", "USER", Schema, 512);

    [Fact]
    public async Task NoApiKey_IsNotConfigured_WithoutSendingAnything()
    {
        var http = new StubHttpClientFactory(_ => AnthropicSse.Message("{}"));

        var thrown = await Calling(http, Options(apiKey: "")).Should().ThrowAsync<LlmUnavailableException>();

        thrown.Which.Provider.Should().Be(LlmProvider.Anthropic);
        thrown.Which.State.Should().Be(LlmState.NotConfigured);
        http.RequestedUrls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized,        LlmState.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden,           LlmState.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests,     LlmState.Busy)]
    [InlineData((HttpStatusCode)529,                LlmState.Busy)]
    [InlineData(HttpStatusCode.InternalServerError, LlmState.Offline)]
    [InlineData(HttpStatusCode.ServiceUnavailable,  LlmState.Offline)]
    [InlineData(HttpStatusCode.NotFound,            LlmState.NotConfigured)]
    public async Task AnAnswerThatProvesClaudeCannotServe_IsUnavailable_NotAFailedRecipe(HttpStatusCode status, LlmState expected)
    {
        var http = new StubHttpClientFactory(_ => AnthropicSse.Error(status));

        var thrown = await Calling(http).Should().ThrowAsync<LlmUnavailableException>();

        thrown.Which.State.Should().Be(expected);
    }

    [Fact]
    public async Task NoConnection_IsOffline()
    {
        var http = new StubHttpClientFactory(_ => throw new HttpRequestException("connection refused"));

        var thrown = await Calling(http).Should().ThrowAsync<LlmUnavailableException>();

        thrown.Which.State.Should().Be(LlmState.Offline);
    }

    /// <summary>A 400 means the app sent something Claude cannot accept, which no retry or other source fixes.</summary>
    [Fact]
    public async Task BadRequest_IsARejectedRequest_ABug_NotAnUnavailableProviderOrAFailedRecipe()
    {
        var http = new StubHttpClientFactory(_ => AnthropicSse.Error(HttpStatusCode.BadRequest, "invalid_request_error"));

        await Calling(http).Should().ThrowAsync<LlmRequestRejectedException>();
    }

    /// <summary>A refusal that survives the fallback, or an answer cut off at the limit, is this source failing to extract.</summary>
    [Theory]
    [InlineData("refusal")]
    [InlineData("max_tokens")]
    public async Task AStopBeforeTheAnswerIsFinished_IsAFailedExtraction(string stopReason)
    {
        var http = new StubHttpClientFactory(_ => AnthropicSse.Message("""{"name":""", stopReason));

        var thrown = await Calling(http).Should().ThrowAsync<InvalidOperationException>();

        thrown.Which.Message.Should().Contain(stopReason);
    }

    [Fact]
    public async Task NoAnswerWithinTheTimeout_IsATimeout_NotACancellation()
    {
        var http = new StubHttpClientFactory(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return AnthropicSse.Message("{}");
        });
        var options = new LlmOptions
        {
            Anthropic = new AnthropicOptions { ApiKey = "sk-ant-test", MaxRetries = 0, TimeoutSeconds = 1 },
        };

        await Calling(http, options).Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task TheCallerCancelling_IsACancellation()
    {
        var http = new StubHttpClientFactory(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return AnthropicSse.Message("{}");
        });
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = () => Client(http).CompleteStructuredAsync("SYSTEM", "USER", Schema, 512, cancel.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Request_OptsIntoTheServerSideRefusalFallback()
    {
        var (request, body, _) = await CaptureAsync();

        body["fallbacks"]!.GetValue<string>().Should().Be("default");
        string.Join(",", request.Headers.GetValues("anthropic-beta"))
            .Should().Contain("server-side-fallback-2026-07-01");
    }
}
