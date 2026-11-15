using Anthropic.Exceptions;
using Microsoft.Extensions.Options;

namespace RecipeApp.API.Services.Llm;

/// <summary>Asks the Models API for the configured model, which proves the key and the model name without spending tokens.</summary>
public sealed class AnthropicStatusProbe(IHttpClientFactory httpClientFactory, IOptions<LlmOptions> options)
    : ILlmStatusProbe
{
    private readonly AnthropicOptions _options = options.Value.Anthropic;

    public async Task<LlmStatus> GetStatusAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return Status(LlmState.NotConfigured);

        // No retries: a status check answers in StatusTimeoutSeconds or reports what it saw.
        var client = AnthropicConnection.Create(httpClientFactory, _options, maxRetries: 0);

        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(_options.StatusTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);

        try
        {
            var model = await client.Models.Retrieve(_options.Model, cancellationToken: linked.Token);
            return Status(LlmState.Ready, model.ID);
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return Status(LlmState.Offline);
        }
        catch (Exception ex) when (AnthropicConnection.UnavailableStateOf(ex) is { } state)
        {
            return Status(state);
        }
        catch (AnthropicApiException)
        {
            return Status(LlmState.Offline);
        }
    }

    private static LlmStatus Status(LlmState state, string? model = null) => new(LlmProvider.Anthropic, state, model);
}
