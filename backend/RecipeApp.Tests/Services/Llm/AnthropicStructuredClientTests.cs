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

    private static readonly LlmCallContext Context =
        new(LlmOperation.ScrapeUrl, LlmStep.Extraction, "https://example.test/soup", Guid.NewGuid());

    private static LlmOptions Options(string apiKey = "sk-ant-test") => new()
    {
        Provider  = LlmProvider.Anthropic,
        Anthropic = new AnthropicOptions { ApiKey = apiKey, MaxRetries = 0 },
    };

    private static AnthropicStructuredClient Client(StubHttpClientFactory http, LlmOptions? options = null) =>
        new(http, Microsoft.Extensions.Options.Options.Create(options ?? Options()));

    private static async Task<(HttpRequestMessage Request, JsonNode Body, LlmCompletion Answer)> CaptureAsync(int maxTokens = 4096)
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var http = new StubHttpClientFactory(request =>
        {
            captured = request;
            body     = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return AnthropicSse.Message("""{"name":"Soup","note":null}""");
        });

        var answer = await Client(http).CompleteStructuredAsync("SYSTEM", "USER", Schema, maxTokens, Context);

        return (captured!, JsonNode.Parse(body!)!, answer);
    }

    // ── The request ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Completion_ReturnsTheTextBlockAsJson_SkippingTheThinkingBlock()
    {
        var (_, _, answer) = await CaptureAsync();

        answer.Json["name"]!.GetValue<string>().Should().Be("Soup");
    }

    /// <summary>Claude's input_tokens already excludes the cached tokens, so the three input counts are kept apart and price separately.</summary>
    [Fact]
    public async Task Completion_ReportsTheAnsweringModelsTokens_WithTheCacheCountsApart()
    {
        var http = new StubHttpClientFactory(_ => AnthropicSse.Message(
            """{"name":"Soup","note":null}""", model: "claude-fallback-model",
            inputTokens: 1200, cacheReadTokens: 300, cacheWriteTokens: 40, outputTokens: 950));

        var answer = await Client(http).CompleteStructuredAsync("SYSTEM", "USER", Schema, 512, Context);

        answer.Usage.Should().Be(new LlmUsage(
            LlmProvider.Anthropic, "claude-fallback-model",
            InputTokens: 1200, CacheReadTokens: 300, CacheWriteTokens: 40, OutputTokens: 950, StopReason: "end_turn"));
    }

    /// <summary>A cut-off or refused answer is still billed, so the failure carries what it cost.</summary>
    [Fact]
    public async Task AStopBeforeTheAnswerIsFinished_CarriesItsUsage()
    {
        var http = new StubHttpClientFactory(_ => AnthropicSse.Message(
            """{"name":""", "max_tokens", inputTokens: 500, outputTokens: 12512));

        var thrown = await Calling(http).Should().ThrowAsync<LlmAnswerFailedException>();

        thrown.Which.Usage.Should().BeEquivalentTo(new
        {
            Provider = LlmProvider.Anthropic, Model = "claude-opus-5-5",
            InputTokens = 500, OutputTokens = 12512, StopReason = "max_tokens",
        });
    }

    [Fact]
    public async Task InvalidJson_IsAFailedExtraction_CarryingItsUsage()
    {
        var http = new StubHttpClientFactory(_ => AnthropicSse.Message("not json", outputTokens: 7));

        var thrown = await Calling(http).Should().ThrowAsync<LlmAnswerFailedException>();

        thrown.Which.Usage.OutputTokens.Should().Be(7);
    }

    /// <summary>A status Claude answered with is a call it received, which is recorded, unlike a request that never left the app.</summary>
    [Fact]
    public async Task AnUnavailableAnswer_SaysClaudeAnswered()
    {
        var http = new StubHttpClientFactory(_ => AnthropicSse.Error(HttpStatusCode.TooManyRequests));

        var thrown = await Calling(http).Should().ThrowAsync<LlmUnavailableException>();

        thrown.Which.ProviderAnswered.Should().BeTrue();
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

    private static Func<Task<LlmCompletion>> Calling(StubHttpClientFactory http, LlmOptions? options = null) =>
        () => Client(http, options).CompleteStructuredAsync("SYSTEM", "USER", Schema, 512, Context);

    [Fact]
    public async Task NoApiKey_IsNotConfigured_WithoutSendingAnything()
    {
        var http = new StubHttpClientFactory(_ => AnthropicSse.Message("{}"));

        var thrown = await Calling(http, Options(apiKey: "")).Should().ThrowAsync<LlmUnavailableException>();

        thrown.Which.Provider.Should().Be(LlmProvider.Anthropic);
        thrown.Which.State.Should().Be(LlmState.NotConfigured);
        thrown.Which.ProviderAnswered.Should().BeFalse();
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
        var http = new StubHttpClientFactory(_ =>
            throw new HttpRequestException(HttpRequestError.ConnectionError, "connection refused"));

        var thrown = await Calling(http).Should().ThrowAsync<LlmUnavailableException>();

        thrown.Which.State.Should().Be(LlmState.Offline);
        thrown.Which.ProviderAnswered.Should().BeFalse();
    }

    /// <summary>A stream that breaks once Claude has started may already be billed, so it counts as having reached Claude.</summary>
    [Fact]
    public async Task AConnectionDroppedMidRequest_IsOffline_ButReachedClaude()
    {
        var http = new StubHttpClientFactory(_ =>
            throw new HttpRequestException(HttpRequestError.ResponseEnded, "response ended prematurely", new IOException()));

        var thrown = await Calling(http).Should().ThrowAsync<LlmUnavailableException>();

        thrown.Which.State.Should().Be(LlmState.Offline);
        thrown.Which.ProviderAnswered.Should().BeTrue();
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

        var act = () => Client(http).CompleteStructuredAsync("SYSTEM", "USER", Schema, 512, Context, cancel.Token);

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
