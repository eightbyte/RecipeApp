using System.Text.Json.Nodes;

namespace RecipeApp.API.Services.Llm;

/// <summary>
/// Builds the real client, and so loads the model, on the first completion rather than on construction, so paths that never run
/// inference (Stage 5) never load the weights.
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
