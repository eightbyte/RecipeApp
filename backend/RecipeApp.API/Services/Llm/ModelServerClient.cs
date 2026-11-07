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

    public async Task<JsonNode> CompleteStructuredAsync(
        string systemPrompt,
        string userContent,
        JsonNode jsonSchema,
        int maxTokens,
        CancellationToken ct = default)
    {
        var server = _options.ModelServer;
        if (string.IsNullOrWhiteSpace(server.BaseUrl))
            throw new ModelServerUnavailableException(ModelServerState.Offline);

        var sampling = _options.Sampling;

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
        catch (HttpRequestException)
        {
            // No HTTP answer at all: refused, or dropped mid-generation when the server is stopped (ResponseEnded or a reset).
            throw new ModelServerUnavailableException(ModelServerState.Offline);
        }

        using (response)
        {
            if (UnavailableStateOf(response.StatusCode) is { } unavailable)
                throw new ModelServerUnavailableException(unavailable);

            response.EnsureSuccessStatusCode();

            var answer  = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
            var content = answer?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
                ?? throw new InvalidOperationException("The Model Server's answer had no message content.");

            return ParseAnswer(content.Trim());
        }
    }

    /// <summary>The state a running Model Server's answer proves it cannot serve in (401 rejects the key, 503 is still loading), or null for any other status.</summary>
    internal static ModelServerState? UnavailableStateOf(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized       => ModelServerState.Unauthorized,
        HttpStatusCode.ServiceUnavailable => ModelServerState.Loading,
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

    private static JsonNode ParseAnswer(string json)
    {
        try
        {
            return JsonNode.Parse(json)
                ?? throw new InvalidOperationException("Model returned empty/invalid JSON.");
        }
        catch (JsonException ex)
        {
            // Constrained decoding should make this unreachable, so a failure means the grammar has a hole and the text that got through is the only way to find it.
            throw new InvalidOperationException(
                $"Model returned invalid JSON: {ex.Message} Around it: {Excerpt(json, ex)}", ex);
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
