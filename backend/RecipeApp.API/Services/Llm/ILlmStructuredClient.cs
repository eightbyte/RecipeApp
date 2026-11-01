using System.Text.Json.Nodes;

namespace RecipeApp.API.Services.Llm;

public interface ILlmStructuredClient
{
    /// <summary>Runs a single structured completion and returns a JSON object conforming to <paramref name="jsonSchema"/>, which implementations must guarantee, for example by grammar-constrained decoding.</summary>
    Task<JsonNode> CompleteStructuredAsync(
        string systemPrompt,
        string userContent,
        JsonNode jsonSchema,
        int maxTokens,
        CancellationToken ct = default);
}
