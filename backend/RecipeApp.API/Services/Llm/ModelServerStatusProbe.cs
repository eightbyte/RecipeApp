using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace RecipeApp.API.Services.Llm;

/// <summary>Whether the Model Server can answer and, once it can, the name it reports for the loaded model.</summary>
public sealed record ModelServerStatus(ModelServerState State, string? Model = null);

public interface IModelServerStatusProbe
{
    /// <summary>Asks the Model Server now, uncached, giving up after <see cref="ModelServerOptions.StatusTimeoutSeconds"/>.</summary>
    Task<ModelServerStatus> GetStatusAsync(CancellationToken ct = default);
}

/// <summary>Reads llama-server's /health, which needs no key, then /v1/models, which does, so a wrong key reads as unauthorized and not ready.</summary>
public sealed class ModelServerStatusProbe(IHttpClientFactory httpClientFactory, IOptions<LlmOptions> options)
    : IModelServerStatusProbe
{
    private readonly ModelServerOptions _server = options.Value.ModelServer;

    public async Task<ModelServerStatus> GetStatusAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_server.BaseUrl))
            return new ModelServerStatus(ModelServerState.Offline);

        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(_server.StatusTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);

        var http = httpClientFactory.CreateClient(ModelServerClient.HttpClientName);

        try
        {
            using (var health = await SendAsync(http, "health", linked.Token))
            {
                var loadingOrDown = StateOf(health.StatusCode);
                if (loadingOrDown is { } state) return new ModelServerStatus(state);
            }

            using var models = await SendAsync(http, "v1/models", linked.Token);
            if (StateOf(models.StatusCode) is { } modelsState) return new ModelServerStatus(modelsState);

            return new ModelServerStatus(ModelServerState.Ready, await ReadModelNameAsync(models, linked.Token));
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new ModelServerStatus(ModelServerState.Offline);
        }
        catch (HttpRequestException)
        {
            return new ModelServerStatus(ModelServerState.Offline);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpClient http, string path, CancellationToken ct)
    {
        using var request = ModelServerClient.CreateRequest(HttpMethod.Get, _server, path);
        return await http.SendAsync(request, ct);
    }

    /// <summary>The state a response proves on its own, or null when it is a success and the probe should go on.</summary>
    private static ModelServerState? StateOf(HttpStatusCode status) =>
        status == HttpStatusCode.OK
            ? null
            : ModelServerClient.UnavailableStateOf(status) ?? ModelServerState.Offline;

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
