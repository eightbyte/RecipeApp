using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using RecipeApp.API.Services.Llm;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Services.Llm;

/// <summary>The HTTP contract with llama-server, exercised against a scripted handler so no GPU or network is needed.</summary>
public class ModelServerClientTests
{
    private const string BaseUrl = "http://model-server.test:9931";

    private static readonly JsonNode Schema = JsonNode.Parse("""
        {
          "type": "object",
          "properties": { "name": { "type": "string" } },
          "required": ["name"]
        }
        """)!;

    private static LlmOptions Options(string apiKey = "") => new()
    {
        ModelServer = new ModelServerOptions { BaseUrl = BaseUrl, ApiKey = apiKey },
        Sampling    = new LlmSamplingOptions { Temperature = 0.33f, TopK = 17, TopP = 0.44f, MinP = 0.05f },
    };

    private static ModelServerClient Client(StubHttpClientFactory http, LlmOptions? options = null) =>
        new(http, Microsoft.Extensions.Options.Options.Create(options ?? Options()));

    /// <summary>A chat completion whose message content is <paramref name="content"/>, shaped as llama-server answers.</summary>
    private static HttpResponseMessage Completion(string content) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                choices = new[] { new { message = new { role = "assistant", content }, finish_reason = "stop" } },
            }),
        };

    private static async Task<(HttpRequestMessage Request, JsonNode Body)> CaptureRequestAsync(
        LlmOptions options, string systemPrompt = "SYSTEM", string userContent = "USER", int maxTokens = 512)
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var http = new StubHttpClientFactory(request =>
        {
            captured = request;
            body     = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Completion("""{"name":"Soup"}""");
        });

        await Client(http, options).CompleteStructuredAsync(systemPrompt, userContent, Schema, maxTokens);

        return (captured!, JsonNode.Parse(body!)!);
    }

    // ── The request ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Complete_PostsToChatCompletionsUnderTheBaseUrl()
    {
        var (request, _) = await CaptureRequestAsync(Options());

        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri.Should().Be(new Uri("http://model-server.test:9931/v1/chat/completions"));
    }

    [Fact]
    public async Task Complete_SendsTheSystemAndUserPromptsAsMessages_SoTheServerAppliesTheGgufTemplate()
    {
        var (_, body) = await CaptureRequestAsync(Options(), systemPrompt: "Be terse.", userContent: "Read this page.");

        var messages = body["messages"]!.AsArray();
        messages.Should().HaveCount(2);
        messages[0]!["role"]!.GetValue<string>().Should().Be("system");
        messages[0]!["content"]!.GetValue<string>().Should().Be("Be terse.");
        messages[1]!["role"]!.GetValue<string>().Should().Be("user");
        messages[1]!["content"]!.GetValue<string>().Should().Be("Read this page.");
    }

    /// <summary>The grammar is ours rather than the server's json_schema converter, because ours leaves optional properties out and the prompts depend on that.</summary>
    [Fact]
    public async Task Complete_SendsJsonSchemaGrammarsGbnf_AndNoResponseFormat()
    {
        var (_, body) = await CaptureRequestAsync(Options());

        body["grammar"]!.GetValue<string>().Should().Be(JsonSchemaGrammar.ToGbnf(Schema));
        body.AsObject().ContainsKey("response_format").Should().BeFalse();
        body.AsObject().ContainsKey("json_schema").Should().BeFalse();
    }

    [Fact]
    public async Task Complete_SendsTheConfiguredSamplingWithEveryRequest()
    {
        var (_, body) = await CaptureRequestAsync(Options());

        body["temperature"]!.GetValue<float>().Should().Be(0.33f);
        body["top_k"]!.GetValue<int>().Should().Be(17);
        body["top_p"]!.GetValue<float>().Should().Be(0.44f);
        body["min_p"]!.GetValue<float>().Should().Be(0.05f);
    }

    [Fact]
    public async Task Complete_CapsTheAnswerAtTheCallersTokenBudget_InOneNonStreamedResponse()
    {
        var (_, body) = await CaptureRequestAsync(Options(), maxTokens: 2048);

        body["max_tokens"]!.GetValue<int>().Should().Be(2048);
        body["stream"]!.GetValue<bool>().Should().BeFalse();
    }

    /// <summary>A thinking block would spend the token budget outside the grammar and change the prompt the benchmark measured.</summary>
    [Fact]
    public async Task Complete_TurnsThinkingOffInTheChatTemplate()
    {
        var (_, body) = await CaptureRequestAsync(Options());

        body["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task Complete_SendsTheApiKeyAsABearerToken_WhenOneIsConfigured()
    {
        var (request, _) = await CaptureRequestAsync(Options(apiKey: "tailnet-key"));

        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be("tailnet-key");
    }

    [Fact]
    public async Task Complete_SendsNoAuthorization_WhenNoApiKeyIsConfigured()
    {
        var (request, _) = await CaptureRequestAsync(Options(apiKey: ""));

        request.Headers.Authorization.Should().BeNull();
    }

    // ── An unavailable Model Server ──────────────────────────────────────────

    private static Func<Task> CompleteAgainst(Func<HttpRequestMessage, HttpResponseMessage> respond, LlmOptions? options = null) =>
        () => Client(new StubHttpClientFactory(respond), options)
            .CompleteStructuredAsync("SYSTEM", "USER", Schema, 512);

    [Fact]
    public async Task Complete_ConnectionRefused_ThrowsUnavailableWithStateOffline()
    {
        var act = CompleteAgainst(_ => throw new HttpRequestException(
            HttpRequestError.ConnectionError, "No connection could be made because the target machine actively refused it."));

        (await act.Should().ThrowAsync<ModelServerUnavailableException>())
            .Which.State.Should().Be(ModelServerState.Offline);
    }

    /// <summary>
    /// Measured: stop.ps1 killing llama-server mid-generation ends the open request with no HTTP answer, as ResponseEnded on a
    /// graceful close or Unknown over a reset, and a Scrape caught in it must still read as the server having gone.
    /// </summary>
    [Theory]
    [InlineData(HttpRequestError.ResponseEnded)]
    [InlineData(HttpRequestError.Unknown)]
    public async Task Complete_ConnectionDroppedMidRequest_ThrowsUnavailableWithStateOffline(HttpRequestError error)
    {
        var act = CompleteAgainst(_ => throw new HttpRequestException(
            error, "An existing connection was forcibly closed by the remote host.", new IOException()));

        (await act.Should().ThrowAsync<ModelServerUnavailableException>())
            .Which.State.Should().Be(ModelServerState.Offline);
    }

    [Fact]
    public async Task Complete_Unauthorized_ThrowsUnavailableWithStateUnauthorized()
    {
        var act = CompleteAgainst(_ => StubHttpClientFactory.Status(HttpStatusCode.Unauthorized));

        (await act.Should().ThrowAsync<ModelServerUnavailableException>())
            .Which.State.Should().Be(ModelServerState.Unauthorized);
    }

    /// <summary>llama-server answers 503 while it is still loading the model.</summary>
    [Fact]
    public async Task Complete_ServiceUnavailable_ThrowsUnavailableWithStateLoading()
    {
        var act = CompleteAgainst(_ => StubHttpClientFactory.Status(HttpStatusCode.ServiceUnavailable));

        (await act.Should().ThrowAsync<ModelServerUnavailableException>())
            .Which.State.Should().Be(ModelServerState.Loading);
    }

    [Fact]
    public async Task Complete_NoBaseUrl_ThrowsUnavailableWithStateOffline_WithoutSendingARequest()
    {
        var http    = new StubHttpClientFactory(_ => Completion("""{"name":"Soup"}"""));
        var options = new LlmOptions { ModelServer = new ModelServerOptions { BaseUrl = "" } };

        var act = () => Client(http, options).CompleteStructuredAsync("SYSTEM", "USER", Schema, 512);

        (await act.Should().ThrowAsync<ModelServerUnavailableException>())
            .Which.State.Should().Be(ModelServerState.Offline);
        http.RequestedUrls.Should().BeEmpty();
    }

    /// <summary>A server error is a failed answer, retried like any other, not a sign the server has gone.</summary>
    [Fact]
    public async Task Complete_ServerError_IsAnOrdinaryFailure()
    {
        var act = CompleteAgainst(_ => StubHttpClientFactory.Status(HttpStatusCode.InternalServerError));

        var thrown = await act.Should().ThrowAsync<Exception>();
        thrown.Which.Should().NotBeOfType<ModelServerUnavailableException>();
    }
}
