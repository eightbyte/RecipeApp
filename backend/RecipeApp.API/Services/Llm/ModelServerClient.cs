using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace RecipeApp.API.Services.Llm;

/// <summary>
/// <see cref="ILlmStructuredClient"/> backed by the Model Server, llama.cpp's <c>llama-server</c>, over its chat completions
/// endpoint, where grammar-constrained decoding is the intent rather than a guarantee, so a parse failure is a real path.
/// </summary>
public sealed class ModelServerClient(IHttpClientFactory httpClientFactory, IOptions<LlmOptions> options)
    : ILlmStructuredClient
{
    /// <summary>The named client, registered with no timeout because each caller's own cancellation budget owns the time limit.</summary>
    public const string HttpClientName = "ModelServer";

    private readonly LlmOptions _options = options.Value;

    public async Task<LlmCompletion> CompleteStructuredAsync(
        string systemPrompt,
        string userContent,
        JsonNode jsonSchema,
        int maxTokens,
        LlmCallContext context,
        CancellationToken ct = default)
    {
        var server = _options.ModelServer;
        if (string.IsNullOrWhiteSpace(server.BaseUrl))
            throw new LlmUnavailableException(LlmProvider.ModelServer, LlmState.Offline);

        var sampling = server.Sampling;

        // grammar is llama.cpp's own field, not response_format, because the server's schema converter would let the model emit optional properties.
        var body = new JsonObject
        {
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user",   ["content"] = userContent },
            },
            ["grammar"]     = JsonSchemaGrammar.ToGbnf(jsonSchema),
            ["temperature"] = sampling.Temperature,
            ["top_k"]       = sampling.TopK,
            ["top_p"]       = sampling.TopP,
            ["min_p"]       = sampling.MinP,
            ["max_tokens"]  = maxTokens,
            ["stream"]      = false,
            ["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false },
        };

        using var request = CreateRequest(HttpMethod.Post, server, "v1/chat/completions");
        request.Content = JsonContent.Create(body);

        var http = httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            // No HTTP answer at all: refused, or dropped mid-generation when the server is stopped (ResponseEnded or a reset).
            throw new LlmUnavailableException(LlmProvider.ModelServer, LlmState.Offline,
                providerAnswered: LlmUnavailableException.ReachedProvider(ex));
        }

        using (response)
        {
            if (UnavailableStateOf(response.StatusCode) is { } unavailable)
                throw new LlmUnavailableException(LlmProvider.ModelServer, unavailable, providerAnswered: true);

            response.EnsureSuccessStatusCode();

            var answer  = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
            var usage   = UsageOf(answer);
            var content = answer?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
                ?? throw new LlmAnswerFailedException("The Model Server's answer had no message content.", usage);

            return new LlmCompletion(ParseAnswer(content.Trim(), usage), usage);
        }
    }

    /// <summary>
    /// The answer's usage in Claude's meaning: llama-server's <c>prompt_tokens</c> counts the whole prompt, so the tokens it
    /// served from its prompt cache (<c>timings.cache_n</c>) are taken out of the input count and reported apart.
    /// </summary>
    private static LlmUsage UsageOf(JsonNode? answer)
    {
        var prompt = IntAt(answer?["usage"]?["prompt_tokens"]);
        var cached = IntAt(answer?["timings"]?["cache_n"]);

        return new LlmUsage(
            LlmProvider.ModelServer,
            answer?["model"]?.GetValue<string>(),
            InputTokens:      prompt is { } whole ? Math.Max(0, whole - (cached ?? 0)) : null,
            CacheReadTokens:  cached,
            CacheWriteTokens: null,
            OutputTokens:     IntAt(answer?["usage"]?["completion_tokens"]),
            StopReason:       answer?["choices"]?[0]?["finish_reason"]?.GetValue<string>());
    }

    private static int? IntAt(JsonNode? node) => node?.GetValue<int>();

    /// <summary>The state a running Model Server's answer proves it cannot serve in (401 rejects the key, 503 is still loading), or null for any other status.</summary>
    internal static LlmState? UnavailableStateOf(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized       => LlmState.Unauthorized,
        HttpStatusCode.ServiceUnavailable => LlmState.Loading,
        _                                 => null,
    };

    /// <summary>A request to <paramref name="path"/> under the configured base URL, carrying the API key when one is set.</summary>
    internal static HttpRequestMessage CreateRequest(HttpMethod method, ModelServerOptions server, string path)
    {
        var endpoint = new Uri(new Uri(server.BaseUrl.TrimEnd('/') + "/"), path);
        var request  = new HttpRequestMessage(method, endpoint);

        if (!string.IsNullOrEmpty(server.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.ApiKey);

        return request;
    }

    private static JsonNode ParseAnswer(string json, LlmUsage usage)
    {
        try
        {
            return JsonNode.Parse(json)
                ?? throw new LlmAnswerFailedException("Model returned empty/invalid JSON.", usage);
        }
        catch (JsonException ex)
        {
            // Constrained decoding should make this unreachable, so a failure means the grammar has a hole and the text that got through is the only way to find it.
            throw new LlmAnswerFailedException(
                $"Model returned invalid JSON: {ex.Message} Around it: {Excerpt(json, ex)}", usage, ex);
        }
    }

    /// <summary>The answer either side of the character the parser objected to, so a failure deep into a decode names the text that caused it.</summary>
    internal static string Excerpt(string json, JsonException failure)
    {
        if (json.Length == 0) return "(empty answer)";

        // BytePositionInLine counts bytes and this indexes characters, so it is a starting point and is clamped for non-ASCII answers.
        var position = (int)Math.Clamp(failure.BytePositionInLine ?? 0, 0, json.Length - 1);
        var start    = Math.Max(0, position - ExcerptRadius);
        var end      = Math.Min(json.Length, position + ExcerptRadius);

        var excerpt = json[start..end].ReplaceLineEndings(" ");

        return $"…{excerpt}…";
    }

    /// <summary>Characters of context shown either side of the offending character.</summary>
    private const int ExcerptRadius = 60;
}
