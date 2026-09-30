using System.Text.Json.Nodes;
using RecipeApp.API.Services.Llm;

namespace RecipeApp.Tests.Infrastructure;

/// <summary>
/// An <see cref="ILlmStructuredClient"/> whose answer is decided per call, so a test can script a
/// rejected-then-corrected sequence without a GPU. Every call is recorded, which is how the retry
/// tests assert on attempt counts and the prompt tests read what the model was actually asked.
///
/// <para>Phase 9 §17.3 assumes exactly this seam: CI has no GPU, so nothing in the suite may reach
/// real inference.</para>
/// </summary>
public class StubLlmStructuredClient : ILlmStructuredClient
{
    private readonly Func<int, JsonNode> _respond;

    /// <param name="respond">Called with the 1-based call number; returns that call's answer.</param>
    public StubLlmStructuredClient(Func<int, JsonNode> respond) => _respond = respond;

    /// <summary>Returns the same answer to every call.</summary>
    public StubLlmStructuredClient(JsonNode answer) : this(_ => answer) { }

    public record Call(string SystemPrompt, string UserContent, JsonNode Schema, int MaxTokens);

    /// <summary>Every completion asked for, in order.</summary>
    public List<Call> Calls { get; } = [];

    public int CallCount => Calls.Count;

    /// <summary>An <see cref="ILlmStructuredClient"/> that always throws — a model that will not load.</summary>
    public static StubLlmStructuredClient AlwaysFailing(string message = "model is not loaded") =>
        new(_ => throw new InvalidOperationException(message));

    public Task<JsonNode> CompleteStructuredAsync(
        string systemPrompt,
        string userContent,
        JsonNode jsonSchema,
        int maxTokens,
        CancellationToken ct = default)
    {
        Calls.Add(new Call(systemPrompt, userContent, jsonSchema, maxTokens));
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(_respond(Calls.Count));
    }
}
