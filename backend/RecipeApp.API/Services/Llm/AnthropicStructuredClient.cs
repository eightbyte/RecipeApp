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

    public async Task<JsonNode> CompleteStructuredAsync(
        string systemPrompt,
        string userContent,
        JsonNode jsonSchema,
        int maxTokens,
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
            throw new LlmUnavailableException(LlmProvider.Anthropic, state);
        }
        catch (AnthropicApiException ex)
        {
            throw new LlmRequestRejectedException($"Claude rejected the request: {ex.Message}", ex);
        }

        if (message.StopReason != "end_turn")
            throw new InvalidOperationException($"Claude stopped with '{message.StopReason}' before finishing the answer.");

        var text = string.Concat(message.Content
            .Select(block => block.TryPickText(out var t) ? t.Text : null)
            .OfType<string>());

        try
        {
            return JsonNode.Parse(text)
                ?? throw new InvalidOperationException("Claude returned an empty answer.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Claude returned invalid JSON: {ex.Message}", ex);
        }
    }

    private static Dictionary<string, JsonElement> SchemaProperties(JsonNode schema) =>
        schema.AsObject().ToDictionary(
            p => p.Key,
            p => JsonSerializer.SerializeToElement(p.Value));
}
