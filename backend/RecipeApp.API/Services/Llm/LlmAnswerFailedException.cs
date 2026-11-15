namespace RecipeApp.API.Services.Llm;

/// <summary>
/// The provider answered but the answer is unusable (cut off, refused, invalid JSON), a failed extraction that was still
/// billed, so it carries the usage the answer reported.
/// </summary>
public sealed class LlmAnswerFailedException(string message, LlmUsage usage, Exception? inner = null)
    : InvalidOperationException(message, inner)
{
    public LlmUsage Usage { get; } = usage;
}
