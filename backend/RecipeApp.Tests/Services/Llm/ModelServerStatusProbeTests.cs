using System.Net;
using System.Net.Http.Json;
using RecipeApp.API.Services.Llm;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Services.Llm;

/// <summary>How the probe reads llama-server, whose /health needs no key, so the key is only proven by a second call to an endpoint that does.</summary>
public class ModelServerStatusProbeTests
{
    private const string BaseUrl = "http://model-server.test:9931";

    private static ModelServerStatusProbe Probe(
        StubHttpClientFactory http, string baseUrl = BaseUrl, string apiKey = "", int statusTimeoutSeconds = 3) =>
        new(http, Microsoft.Extensions.Options.Options.Create(new LlmOptions
        {
            ModelServer = new ModelServerOptions
            {
                BaseUrl = baseUrl, ApiKey = apiKey, StatusTimeoutSeconds = statusTimeoutSeconds,
            },
        }));

    /// <summary>The /v1/models answer llama-server gives once loaded, with the start script's --alias as the model id.</summary>
    private static HttpResponseMessage Models(string alias) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { @object = "list", data = new[] { new { id = alias, @object = "model" } } }),
        };

    private static HttpResponseMessage Healthy() => StubHttpClientFactory.Ok("""{"status":"ok"}""", "application/json");

    [Fact]
    public async Task Status_NoBaseUrl_IsOffline_WithoutSendingARequest()
    {
        var http = new StubHttpClientFactory(_ => Healthy());

        var status = await Probe(http, baseUrl: "").GetStatusAsync();

        status.State.Should().Be(ModelServerState.Offline);
        http.RequestedUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task Status_ConnectionRefused_IsOffline()
    {
        var http = new StubHttpClientFactory(_ => throw new HttpRequestException(
            HttpRequestError.ConnectionError, "No connection could be made because the target machine actively refused it."));

        var status = await Probe(http).GetStatusAsync();

        status.State.Should().Be(ModelServerState.Offline);
        status.Model.Should().BeNull();
    }

    [Fact]
    public async Task Status_HealthAnswers503_IsLoading()
    {
        var http = new StubHttpClientFactory(_ => StubHttpClientFactory.Status(HttpStatusCode.ServiceUnavailable));

        var status = await Probe(http).GetStatusAsync();

        status.State.Should().Be(ModelServerState.Loading);
    }

    /// <summary>/health answers 200 with any key, so a wrong key would read as ready without the second call.</summary>
    [Fact]
    public async Task Status_HealthyButTheKeyIsRejected_IsUnauthorized()
    {
        var http = new StubHttpClientFactory(request => request.RequestUri!.AbsolutePath == "/health"
            ? Healthy()
            : StubHttpClientFactory.Status(HttpStatusCode.Unauthorized));

        var status = await Probe(http, apiKey: "wrong-key").GetStatusAsync();

        status.State.Should().Be(ModelServerState.Unauthorized);
    }

    [Fact]
    public async Task Status_HealthyAndTheKeyIsAccepted_IsReady_NamingTheLoadedModel()
    {
        string? authorization = null;
        var http = new StubHttpClientFactory(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/health") return Healthy();

            authorization = request.Headers.Authorization?.ToString();
            return Models("Qwen3.5-9B-Q6_K.gguf");
        });

        var status = await Probe(http, apiKey: "tailnet-key").GetStatusAsync();

        status.State.Should().Be(ModelServerState.Ready);
        status.Model.Should().Be("Qwen3.5-9B-Q6_K.gguf");
        authorization.Should().Be("Bearer tailnet-key");
        http.RequestedUrls.Should().Equal(
            "http://model-server.test:9931/health", "http://model-server.test:9931/v1/models");
    }

    [Fact]
    public async Task Status_ReadyWithAModelListItCannotRead_IsStillReady_WithNoModelName()
    {
        var http = new StubHttpClientFactory(request => request.RequestUri!.AbsolutePath == "/health"
            ? Healthy()
            : StubHttpClientFactory.Ok("""{"data":[]}""", "application/json"));

        var status = await Probe(http).GetStatusAsync();

        status.State.Should().Be(ModelServerState.Ready);
        status.Model.Should().BeNull();
    }

    /// <summary>The sheet waits on this answer, so a server that accepts the connection and never replies must not hold it.</summary>
    [Fact]
    public async Task Status_NoAnswerWithinTheStatusTimeout_IsOffline()
    {
        var http = new StubHttpClientFactory(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Healthy();
        });

        var started = DateTime.UtcNow;
        var status  = await Probe(http, statusTimeoutSeconds: 1).GetStatusAsync();

        status.State.Should().Be(ModelServerState.Offline);
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(10));
    }
}
