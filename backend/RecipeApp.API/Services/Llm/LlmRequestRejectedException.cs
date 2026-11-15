namespace RecipeApp.API.Services.Llm;

/// <summary>The provider refused the request itself as malformed, a bug in what the app sent, so callers let it surface as a 500 instead of blaming the source.</summary>
public sealed class LlmRequestRejectedException(string message, Exception inner) : Exception(message, inner);
