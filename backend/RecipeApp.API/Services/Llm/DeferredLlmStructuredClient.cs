using System.Text.Json.Nodes;

namespace RecipeApp.API.Services.Llm;

/// <summary>
/// Builds the real client — and so loads the model — on the first completion rather than on
/// construction.
///
/// <para><see cref="LlamaModelHolder"/> loads the GGUF weights in its constructor, and
/// <c>RecipeScrapeService</c> takes an <see cref="ILlmStructuredClient"/> even though its
/// <c>ConfirmAsync</c> never runs inference. Without this, Phase 9 Stage 5 — which persists through
/// <c>ConfirmAsync</c> and calls no model at all — would load 9B parameters onto the GPU per run
/// only to discard them, and would fail outright on a machine with no model configured. The web
/// host is unaffected: it resolves the holder eagerly at startup so a bad path still surfaces
/// there.</para>
/// </summary>
public sealed class DeferredLlmStructuredClient(Func<ILlmStructuredClient> createClient)
    : ILlmStructuredClient
{
    private readonly Lazy<ILlmStructuredClient> _client = new(createClient);

    public Task<JsonNode> CompleteStructuredAsync(
        string systemPrompt,
        string userContent,
        JsonNode jsonSchema,
        int maxTokens,
        CancellationToken ct = default) =>
        _client.Value.CompleteStructuredAsync(systemPrompt, userContent, jsonSchema, maxTokens, ct);
}
