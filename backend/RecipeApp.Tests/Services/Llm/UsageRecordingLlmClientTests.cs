using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using RecipeApp.API.Models;
using RecipeApp.API.Services.Llm;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Services.Llm;

/// <summary>Every call through a provider is recorded once with its Outcome, tokens and cost, and recording never changes what the caller sees.</summary>
public class UsageRecordingLlmClientTests
{
    private static readonly JsonNode Schema = JsonNode.Parse("""{ "type": "object" }""")!;
    private static readonly JsonNode Answer = JsonNode.Parse("""{ "name": "Soup" }""")!;

    private static readonly LlmCallContext Context =
        new(LlmOperation.ScrapeText, LlmStep.IngredientMatching, "https://example.test/soup", Guid.NewGuid());

    private static readonly LlmOptions Options = new()
    {
        Anthropic = new AnthropicOptions { Model = "claude-opus-5-5" },
        Pricing   = new Dictionary<string, LlmModelPrice>
        {
            ["claude-opus-5-5"] = new() { Input = 4.00m, Output = 20.00m, CacheRead = 0.20m, CacheWrite = 5.00m },
        },
    };

    private static LlmUsage ClaudeUsage(string model = "claude-opus-5-5") =>
        new(LlmProvider.Anthropic, model,
            InputTokens: 1200, CacheReadTokens: 300, CacheWriteTokens: 40, OutputTokens: 950, StopReason: "end_turn");

    private sealed class ScriptedClient(Func<Task<LlmCompletion>> respond) : ILlmStructuredClient
    {
        public Task<LlmCompletion> CompleteStructuredAsync(
            string systemPrompt, string userContent, JsonNode jsonSchema, int maxTokens,
            LlmCallContext context, CancellationToken ct = default) => respond();
    }

    private sealed class InMemoryRecorder(Exception? failWith = null) : ILlmCallRecorder
    {
        public List<LlmCall> Calls { get; } = [];

        public Task RecordAsync(LlmCall call, CancellationToken ct = default)
        {
            if (failWith is not null) throw failWith;
            Calls.Add(call);
            return Task.CompletedTask;
        }
    }

    private readonly InMemoryRecorder _recorder = new();
    private readonly RecordingLogger<UsageRecordingLlmClient> _logger = new();

    private UsageRecordingLlmClient Decorating(
        LlmProvider provider, Func<Task<LlmCompletion>> respond, ILlmCallRecorder? recorder = null) =>
        new(new ScriptedClient(respond), provider, recorder ?? _recorder,
            Microsoft.Extensions.Options.Options.Create(Options), _logger);

    private Task<LlmCompletion> Call(LlmProvider provider, Func<Task<LlmCompletion>> respond) =>
        Decorating(provider, respond).CompleteStructuredAsync("SYSTEM", "USER", Schema, 512, Context);

    private LlmCall Recorded => _recorder.Calls.Should().ContainSingle().Which;

    // ── A usable answer ──────────────────────────────────────────────────────

    [Fact]
    public async Task ASucceededCall_IsRecordedWithItsContextAndUsage_AndReturnedUnchanged()
    {
        var completion = new LlmCompletion(Answer, StubLlmStructuredClient.Usage);

        var returned = await Call(LlmProvider.ModelServer, () => Task.FromResult(completion));

        returned.Should().BeSameAs(completion);
        Recorded.Should().BeEquivalentTo(new
        {
            Provider         = LlmProvider.ModelServer,
            Model            = "stub-model.gguf",
            Operation        = LlmOperation.ScrapeText,
            Step             = LlmStep.IngredientMatching,
            CorrelationId    = Context.CorrelationId,
            SourceUrl        = "https://example.test/soup",
            Outcome          = LlmCallOutcome.Succeeded,
            InputTokens      = 100,
            CacheReadTokens  = 20,
            CacheWriteTokens = (int?)null,
            OutputTokens     = 50,
        });
        Recorded.Id.Should().NotBeEmpty();
        Recorded.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task AModelServerCall_CostsNothing()
    {
        await Call(LlmProvider.ModelServer, () => Task.FromResult(new LlmCompletion(Answer, StubLlmStructuredClient.Usage)));

        Recorded.EstimatedCostUsd.Should().Be(0m);
    }

    /// <summary>1200 × $4 + 950 × $20 + 300 × $0.20 + 40 × $5, per million tokens.</summary>
    [Fact]
    public async Task AClaudeCall_IsPricedPerColumnFromTheAnsweringModelsEntry()
    {
        await Call(LlmProvider.Anthropic, () => Task.FromResult(new LlmCompletion(Answer, ClaudeUsage())));

        Recorded.EstimatedCostUsd.Should().Be(0.02406m);
    }

    /// <summary>The refusal fallback can answer on a model the price table has never heard of, and a guessed cost would mislead.</summary>
    [Fact]
    public async Task AnAnsweringModelWithNoPriceEntry_RecordsANullCost()
    {
        await Call(LlmProvider.Anthropic, () => Task.FromResult(new LlmCompletion(Answer, ClaudeUsage("claude-unpriced-fallback"))));

        Recorded.Model.Should().Be("claude-unpriced-fallback");
        Recorded.EstimatedCostUsd.Should().BeNull();
    }

    [Fact]
    public async Task TheDurationIsTheWallClockAroundTheWholeCall()
    {
        // Polled rather than one Task.Delay, which Windows' timer resolution can end a few milliseconds early.
        await Call(LlmProvider.ModelServer, async () =>
        {
            var held = System.Diagnostics.Stopwatch.StartNew();
            while (held.ElapsedMilliseconds < 50) await Task.Delay(5);
            return new LlmCompletion(Answer, StubLlmStructuredClient.Usage);
        });

        Recorded.DurationMs.Should().BeGreaterThanOrEqualTo(50);
    }

    // ── Failures ─────────────────────────────────────────────────────────────

    /// <summary>A cut-off or refused answer was billed, so its tokens and cost are kept.</summary>
    [Fact]
    public async Task AnUnusableAnswer_IsAFailedExtraction_WithItsTokens_AndRethrownUnchanged()
    {
        var failure = new LlmAnswerFailedException("stopped with max_tokens", ClaudeUsage() with { StopReason = "max_tokens" });

        var act = () => Call(LlmProvider.Anthropic, () => throw failure);

        (await act.Should().ThrowAsync<LlmAnswerFailedException>()).Which.Should().BeSameAs(failure);
        Recorded.Should().BeEquivalentTo(new
        {
            Outcome = LlmCallOutcome.FailedExtraction, InputTokens = 1200, OutputTokens = 950, EstimatedCostUsd = 0.02406m,
        });
    }

    /// <summary>A Model Server 500 reached the provider but reported no usage.</summary>
    [Fact]
    public async Task AnyOtherFailure_IsAFailedExtraction_WithNoTokens()
    {
        var act = () => Call(LlmProvider.ModelServer, () => throw new HttpRequestException("500 Internal Server Error"));

        await act.Should().ThrowAsync<HttpRequestException>();
        Recorded.Should().BeEquivalentTo(new
        {
            Outcome = LlmCallOutcome.FailedExtraction, InputTokens = (int?)null, OutputTokens = (int?)null, EstimatedCostUsd = 0m,
        });
    }

    /// <summary>A cancelled stream reports no usage; with nothing answering, Claude's row names the model it asked for.</summary>
    [Theory]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(OperationCanceledException))]
    public async Task ATimeout_RecordsItsDurationWithNoTokens_UnderTheConfiguredModel(Type exceptionType)
    {
        var act = () => Call(LlmProvider.Anthropic, () => throw (Exception)Activator.CreateInstance(exceptionType)!);

        await act.Should().ThrowAsync<Exception>();
        Recorded.Should().BeEquivalentTo(new
        {
            Outcome          = LlmCallOutcome.Timeout,
            Model            = "claude-opus-5-5",
            InputTokens      = (int?)null,
            CacheReadTokens  = (int?)null,
            CacheWriteTokens = (int?)null,
            OutputTokens     = (int?)null,
            EstimatedCostUsd = 0m,
        });
    }

    [Fact]
    public async Task AModelServerTimeout_NamesNoModel()
    {
        var act = () => Call(LlmProvider.ModelServer, () => throw new OperationCanceledException());

        await act.Should().ThrowAsync<OperationCanceledException>();
        Recorded.Should().BeEquivalentTo(new { Outcome = LlmCallOutcome.Timeout, Model = (string?)null, EstimatedCostUsd = 0m });
    }

    [Fact]
    public async Task AProviderAnsweringThatItCannotServe_IsRecordedAsUnavailable()
    {
        var act = () => Call(LlmProvider.Anthropic,
            () => throw new LlmUnavailableException(LlmProvider.Anthropic, LlmState.Busy, providerAnswered: true));

        await act.Should().ThrowAsync<LlmUnavailableException>();
        Recorded.Outcome.Should().Be(LlmCallOutcome.Unavailable);
    }

    /// <summary>No key, no base URL or no connection: nothing reached the provider, so there is nothing to record.</summary>
    [Fact]
    public async Task ARequestThatNeverReachedTheProvider_RecordsNothing()
    {
        var act = () => Call(LlmProvider.Anthropic,
            () => throw new LlmUnavailableException(LlmProvider.Anthropic, LlmState.NotConfigured));

        await act.Should().ThrowAsync<LlmUnavailableException>();
        _recorder.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ARequestTheProviderRefusedAsMalformed_IsRecordedAsRejected()
    {
        var act = () => Call(LlmProvider.Anthropic,
            () => throw new LlmRequestRejectedException("400", new InvalidOperationException()));

        await act.Should().ThrowAsync<LlmRequestRejectedException>();
        Recorded.Outcome.Should().Be(LlmCallOutcome.Rejected);
    }

    // ── Recording never fails a call ─────────────────────────────────────────

    [Fact]
    public async Task ARecorderFailure_IsLogged_AndTheAnswerStillReturned()
    {
        var completion = new LlmCompletion(Answer, StubLlmStructuredClient.Usage);
        var client     = Decorating(LlmProvider.ModelServer, () => Task.FromResult(completion),
            new InMemoryRecorder(failWith: new InvalidOperationException("database is down")));

        var returned = await client.CompleteStructuredAsync("SYSTEM", "USER", Schema, 512, Context);

        returned.Should().BeSameAs(completion);
        _logger.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("LLM Call"));
    }

    [Fact]
    public async Task ARecorderFailure_LeavesTheCallsOwnFailureAsItWas()
    {
        var failure = new LlmAnswerFailedException("invalid JSON", StubLlmStructuredClient.Usage);
        var client  = Decorating(LlmProvider.ModelServer, () => throw failure,
            new InMemoryRecorder(failWith: new InvalidOperationException("database is down")));

        var act = () => client.CompleteStructuredAsync("SYSTEM", "USER", Schema, 512, Context);

        (await act.Should().ThrowAsync<LlmAnswerFailedException>()).Which.Should().BeSameAs(failure);
    }
}
