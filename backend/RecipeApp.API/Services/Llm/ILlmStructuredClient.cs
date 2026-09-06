using System.Text.Json.Nodes;

namespace RecipeApp.API.Services.Llm;

public interface ILlmStructuredClient
{
    /// <summary>
    /// Runs a single structured completion and returns a JSON object conforming to
    /// <paramref name="jsonSchema"/>. Implementations must guarantee schema validity
    /// (e.g. grammar-constrained decoding) — callers may deserialize the result directly.
    /// </summary>
    Task<JsonNode> CompleteStructuredAsync(
        string systemPrompt,
        string userContent,
        JsonNode jsonSchema,
        int maxTokens,
        CancellationToken ct = default);
}
