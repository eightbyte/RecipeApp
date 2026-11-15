using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace RecipeApp.API.Services.Llm;

/// <summary>Reads llama-server's /health, which needs no key, then /v1/models, which does, so a wrong key reads as unauthorized and not ready.</summary>
public sealed class ModelServerStatusProbe(IHttpClientFactory httpClientFactory, IOptions<LlmOptions> options)
    : ILlmStatusProbe
{
    private readonly ModelServerOptions _server = options.Value.ModelServer;

    public async Task<LlmStatus> GetStatusAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_server.BaseUrl))
            return Status(LlmState.Offline);

        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(_server.StatusTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);

        var http = httpClientFactory.CreateClient(ModelServerClient.HttpClientName);

        try
        {
            using (var health = await SendAsync(http, "health", linked.Token))
            {
                var loadingOrDown = StateOf(health.StatusCode);
                if (loadingOrDown is { } state) return Status(state);
            }

            using var models = await SendAsync(http, "v1/models", linked.Token);
            if (StateOf(models.StatusCode) is { } modelsState) return Status(modelsState);

            return Status(LlmState.Ready, await ReadModelNameAsync(models, linked.Token));
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return Status(LlmState.Offline);
        }
        catch (HttpRequestException)
        {
            return Status(LlmState.Offline);
        }
    }

    private static LlmStatus Status(LlmState state, string? model = null) => new(LlmProvider.ModelServer, state, model);

    private async Task<HttpResponseMessage> SendAsync(HttpClient http, string path, CancellationToken ct)
    {
        using var request = ModelServerClient.CreateRequest(HttpMethod.Get, _server, path);
        return await http.SendAsync(request, ct);
    }

    /// <summary>The state a response proves on its own, or null when it is a success and the probe should go on.</summary>
    private static LlmState? StateOf(HttpStatusCode status) =>
        status == HttpStatusCode.OK
            ? null
            : ModelServerClient.UnavailableStateOf(status) ?? LlmState.Offline;

    /// <summary>The first model's id, which is the start script's --alias, or null when the list is not in the shape llama-server sends.</summary>
    private static async Task<string?> ReadModelNameAsync(HttpResponseMessage models, CancellationToken ct)
    {
        try
        {
            var list = JsonNode.Parse(await models.Content.ReadAsStringAsync(ct));
            return list?["data"] is JsonArray { Count: > 0 } data
                ? data[0]?["id"]?.GetValue<string>()
                : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
