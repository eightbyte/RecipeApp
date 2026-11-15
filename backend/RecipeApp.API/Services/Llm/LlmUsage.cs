using System.Text.Json.Nodes;

namespace RecipeApp.API.Services.Llm;

/// <summary>
/// What one answer cost in tokens, in Claude's meaning for both providers: <see cref="InputTokens"/> excludes the cached
/// tokens, so the whole prompt is the sum of the three input columns.
/// </summary>
/// <param name="Model">The model that answered, which under Claude's refusal fallback can differ from the configured one.</param>
/// <param name="CacheWriteTokens">Null for the Model Server, which has no paid cache write.</param>
/// <param name="OutputTokens">Thinking included.</param>
public sealed record LlmUsage(
    LlmProvider Provider,
    string? Model,
    int? InputTokens,
    int? CacheReadTokens,
    int? CacheWriteTokens,
    int? OutputTokens,
    string? StopReason);

/// <summary>An answer that conforms to the schema, with what it cost.</summary>
public sealed record LlmCompletion(JsonNode Json, LlmUsage Usage);
