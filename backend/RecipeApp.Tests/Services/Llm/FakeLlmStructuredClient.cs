using System.Text.Json.Nodes;
using RecipeApp.API.Services.Llm;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Services.Llm;

/// <summary>Returns canned JsonNode responses in order, with no model loaded.</summary>
public class FakeLlmStructuredClient : ILlmStructuredClient
{
    private readonly Queue<JsonNode> _responses;

    public FakeLlmStructuredClient(params JsonNode[] responses)
    {
        _responses = new Queue<JsonNode>(responses);
    }

    public int CallCount { get; private set; }

    public Task<LlmCompletion> CompleteStructuredAsync(
        string systemPrompt,
        string userContent,
        JsonNode jsonSchema,
        int maxTokens,
        LlmCallContext context,
        CancellationToken ct = default)
    {
        CallCount++;
        if (_responses.Count == 0)
            throw new InvalidOperationException("FakeLlmStructuredClient has no more canned responses.");

        return Task.FromResult(new LlmCompletion(_responses.Dequeue(), StubLlmStructuredClient.Usage));
    }
}
