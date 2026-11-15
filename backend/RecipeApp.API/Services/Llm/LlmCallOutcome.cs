using System.Text.Json.Serialization;

namespace RecipeApp.API.Services.Llm;

/// <summary>How an LLM Call that reached its provider ended.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LlmCallOutcome>))]
public enum LlmCallOutcome
{
    Succeeded,

    /// <summary>The provider answered, but not usably (cut off, refused, invalid JSON, a server error).</summary>
    FailedExtraction,

    /// <summary>A budget ran out or the caller gave up, before any usage came back.</summary>
    Timeout,

    /// <summary>The provider answered that it cannot serve (unauthorized, loading, busy).</summary>
    Unavailable,

    /// <summary>The provider refused the request itself as malformed.</summary>
    Rejected,
}
