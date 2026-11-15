using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using RecipeApp.API.Models;

namespace RecipeApp.API.Services.Llm;

/// <summary>
/// Wraps one provider's client and records every call that reached the provider as an LLM Call, with its Outcome, tokens
/// and estimated cost, then returns or rethrows exactly what the client did, because recording must never fail a call.
/// </summary>
/// <param name="provider">The wrapped client's provider, which a row with no usage still needs.</param>
public sealed class UsageRecordingLlmClient(
    ILlmStructuredClient inner,
    LlmProvider provider,
    ILlmCallRecorder recorder,
    IOptions<LlmOptions> options,
    ILogger<UsageRecordingLlmClient> logger) : ILlmStructuredClient
{
    private readonly LlmOptions _options = options.Value;

    /// <summary>The wrapped client, exposed so a registration test can see which provider answers.</summary>
    internal ILlmStructuredClient Inner => inner;

    public async Task<LlmCompletion> CompleteStructuredAsync(
        string systemPrompt,
        string userContent,
        JsonNode jsonSchema,
        int maxTokens,
        LlmCallContext context,
        CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var completion = await inner.CompleteStructuredAsync(systemPrompt, userContent, jsonSchema, maxTokens, context, ct);
            await RecordAsync(context, LlmCallOutcome.Succeeded, completion.Usage, started);
            return completion;
        }
        catch (Exception ex) when (OutcomeOf(ex) is { } outcome)
        {
            await RecordAsync(context, outcome, (ex as LlmAnswerFailedException)?.Usage, started);
            throw;
        }
    }

    /// <summary>The Outcome a failure records, or null for a request that never reached the provider.</summary>
    private static LlmCallOutcome? OutcomeOf(Exception failure) => failure switch
    {
        LlmUnavailableException { ProviderAnswered: false }  => null,
        LlmUnavailableException                              => LlmCallOutcome.Unavailable,
        LlmRequestRejectedException                          => LlmCallOutcome.Rejected,
        TimeoutException or OperationCanceledException       => LlmCallOutcome.Timeout,
        _                                                    => LlmCallOutcome.FailedExtraction,
    };

    private async Task RecordAsync(LlmCallContext context, LlmCallOutcome outcome, LlmUsage? usage, long started)
    {
        // Claude was asked for a known model, so a call nothing answered is still grouped and priced under it.
        var model = usage is not null ? usage.Model : provider == LlmProvider.Anthropic ? _options.Anthropic.Model : null;

        var call = new LlmCall
        {
            Id               = Guid.NewGuid(),
            CreatedAt        = DateTime.UtcNow,
            Provider         = provider,
            Model            = model,
            Operation        = context.Operation,
            Step             = context.Step,
            CorrelationId    = context.CorrelationId,
            SourceUrl        = context.SourceUrl,
            Outcome          = outcome,
            DurationMs       = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            InputTokens      = usage?.InputTokens,
            CacheReadTokens  = usage?.CacheReadTokens,
            CacheWriteTokens = usage?.CacheWriteTokens,
            OutputTokens     = usage?.OutputTokens,
            EstimatedCostUsd = EstimateCostUsd(model, usage),
        };

        try
        {
            // Not the caller's token: a timed-out call is cancelled by then, and its row matters most.
            await recorder.RecordAsync(call, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not record the LLM Call for {Operation} ({Outcome}); the call itself carries on.",
                context.Operation, outcome);
        }
    }

    /// <summary>A straight per-column sum at the model's list price, 0 for the Model Server, and null for a model with no price entry.</summary>
    private decimal? EstimateCostUsd(string? model, LlmUsage? usage)
    {
        if (provider == LlmProvider.ModelServer) return 0m;
        if (model is null || !_options.Pricing.TryGetValue(model, out var price)) return null;

        var perMillion = (usage?.InputTokens      ?? 0) * price.Input
                       + (usage?.OutputTokens     ?? 0) * price.Output
                       + (usage?.CacheReadTokens  ?? 0) * price.CacheRead
                       + (usage?.CacheWriteTokens ?? 0) * price.CacheWrite;

        return perMillion / TokensPerPrice;
    }

    /// <summary>Prices are quoted per million tokens.</summary>
    private const decimal TokensPerPrice = 1_000_000m;
}
