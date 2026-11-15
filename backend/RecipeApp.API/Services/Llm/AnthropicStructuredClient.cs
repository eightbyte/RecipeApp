using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Options;

namespace RecipeApp.API.Services.Llm;

/// <summary>
/// <see cref="ILlmStructuredClient"/> backed by the Claude API, constraining the answer with structured outputs rather
/// than tool use, and streaming the request so a long answer never meets an HTTP timeout.
/// </summary>
public sealed class AnthropicStructuredClient(IHttpClientFactory httpClientFactory, IOptions<LlmOptions> options)
    : ILlmStructuredClient
{
    /// <summary>The named client, registered with no timeout because <see cref="AnthropicOptions.TimeoutSeconds"/> owns the limit.</summary>
    public const string HttpClientName = "Anthropic";

    /// <summary>Re-serves a policy refusal on the server-defined fallback model inside the same call.</summary>
    private const string ServerSideFallbackBeta = "server-side-fallback-2026-07-01";

    private readonly AnthropicOptions _options = options.Value.Anthropic;

    public async Task<LlmCompletion> CompleteStructuredAsync(
        string systemPrompt,
        string userContent,
        JsonNode jsonSchema,
        int maxTokens,
        LlmCallContext context,
        CancellationToken ct = default)
    {
        var client = AnthropicConnection.Create(httpClientFactory, _options, _options.MaxRetries);

        var request = new MessageCreateParams
        {
            Model     = _options.Model,
            MaxTokens = maxTokens + _options.ThinkingHeadroomTokens,
            System    = systemPrompt,
            Messages  = [new BetaUserMessageParam { Content = userContent }],
            OutputConfig = new BetaOutputConfig
            {
                Effort = JsonSerializer.Deserialize<Effort>(JsonSerializer.Serialize(_options.Effort)),
                Format = new BetaJsonOutputFormat { Schema = SchemaProperties(StructuredOutputSchema.Fit(jsonSchema)) },
            },
            Betas     = [ServerSideFallbackBeta],
            Fallbacks = new Default(),
        };

        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);

        BetaMessage message;
        try
        {
            message = await client.Beta.Messages.CreateStreaming(request, linked.Token).Aggregate();
        }
        catch (OperationCanceledException ex) when (budget.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Claude gave no answer within {_options.TimeoutSeconds}s.", ex);
        }
        catch (Exception ex) when (AnthropicConnection.UnavailableStateOf(ex) is { } state)
        {
            throw new LlmUnavailableException(LlmProvider.Anthropic, state,
                providerAnswered: ex is not (AnthropicIOException or HttpRequestException)
                                  || LlmUnavailableException.ReachedProvider(ex));
        }
        catch (AnthropicApiException ex)
        {
            throw new LlmRequestRejectedException($"Claude rejected the request: {ex.Message}", ex);
        }

        var usage = UsageOf(message);

        if (message.StopReason != "end_turn")
            throw new LlmAnswerFailedException(
                $"Claude stopped with '{usage.StopReason}' before finishing the answer.", usage);

        var text = string.Concat(message.Content
            .Select(block => block.TryPickText(out var t) ? t.Text : null)
            .OfType<string>());

        try
        {
            var json = JsonNode.Parse(text)
                ?? throw new LlmAnswerFailedException("Claude returned an empty answer.", usage);
            return new LlmCompletion(json, usage);
        }
        catch (JsonException ex)
        {
            throw new LlmAnswerFailedException($"Claude returned invalid JSON: {ex.Message}", usage, ex);
        }
    }

    /// <summary>What the final message reports, its own input count already excluding the cached tokens.</summary>
    private static LlmUsage UsageOf(BetaMessage message) => new(
        LlmProvider.Anthropic,
        message.Model.Raw(),
        InputTokens:      (int)message.Usage.InputTokens,
        CacheReadTokens:  (int)(message.Usage.CacheReadInputTokens ?? 0),
        CacheWriteTokens: (int)(message.Usage.CacheCreationInputTokens ?? 0),
        OutputTokens:     (int)message.Usage.OutputTokens,
        StopReason:       message.StopReason?.Raw());

    private static Dictionary<string, JsonElement> SchemaProperties(JsonNode schema) =>
        schema.AsObject().ToDictionary(
            p => p.Key,
            p => JsonSerializer.SerializeToElement(p.Value));
}
