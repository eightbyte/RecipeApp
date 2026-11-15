using System.Text.Json.Nodes;

namespace RecipeApp.API.Services.Llm;

public interface ILlmStructuredClient
{
    /// <summary>Runs a single structured completion and returns a JSON object conforming to <paramref name="jsonSchema"/>, which implementations must guarantee, for example by grammar-constrained decoding.</summary>
    /// <param name="context">Who is calling and for what, recorded with the LLM Call.</param>
    /// <exception cref="LlmAnswerFailedException">The provider answered, but not with a usable answer.</exception>
    Task<LlmCompletion> CompleteStructuredAsync(
        string systemPrompt,
        string userContent,
        JsonNode jsonSchema,
        int maxTokens,
        LlmCallContext context,
        CancellationToken ct = default);
}
