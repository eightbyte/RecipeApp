using System.Text.Json.Nodes;
using RecipeApp.API.Services.Llm;

namespace RecipeApp.Tests.Services.Llm;

/// <summary>
/// Returns canned JsonNode responses in order. No model is loaded.
/// </summary>
public class FakeLlmStructuredClient : ILlmStructuredClient
{
    private readonly Queue<JsonNode> _responses;

    public FakeLlmStructuredClient(params JsonNode[] responses)
    {
        _responses = new Queue<JsonNode>(responses);
    }

    public int CallCount { get; private set; }

    public Task<JsonNode> CompleteStructuredAsync(
        string systemPrompt,
        string userContent,
        JsonNode jsonSchema,
        int maxTokens,
        CancellationToken ct = default)
    {
        CallCount++;
        if (_responses.Count == 0)
            throw new InvalidOperationException("FakeLlmStructuredClient has no more canned responses.");

        return Task.FromResult(_responses.Dequeue());
    }
}
