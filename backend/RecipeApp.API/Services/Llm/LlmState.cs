using System.Text.Json.Serialization;

namespace RecipeApp.API.Services.Llm;

/// <summary>Whether the LLM provider can answer, as the status endpoint reports it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LlmState>))]
public enum LlmState
{
    /// <summary>Unreachable, or for the Model Server no <see cref="ModelServerOptions.BaseUrl"/> is configured.</summary>
    [JsonStringEnumMemberName("offline")]
    Offline,

    /// <summary>The Model Server is running, but still loading the model.</summary>
    [JsonStringEnumMemberName("loading")]
    Loading,

    [JsonStringEnumMemberName("ready")]
    Ready,

    /// <summary>Reachable, but it rejects the configured API key.</summary>
    [JsonStringEnumMemberName("unauthorized")]
    Unauthorized,

    /// <summary>Reachable, but rate-limited or overloaded for now.</summary>
    [JsonStringEnumMemberName("busy")]
    Busy,

    /// <summary>A cloud provider with no API key, or a model it does not know.</summary>
    [JsonStringEnumMemberName("notConfigured")]
    NotConfigured,
}

public static class LlmStateExtensions
{
    /// <summary>Why inference is unavailable in this state, a lower-case clause that follows "… is unavailable: " so every caller, and the frontend, words it the same way.</summary>
    public static string UnavailableReason(this LlmState state, LlmProvider provider) => provider switch
    {
        LlmProvider.Anthropic => state switch
        {
            LlmState.Unauthorized  => "Claude (Anthropic) rejected the API key.",
            LlmState.Busy          => "Claude is busy right now, try again in a moment.",
            LlmState.NotConfigured => "Claude (Anthropic) isn't configured.",
            _                      => "Claude (Anthropic) can't be reached.",
        },
        _ => state switch
        {
            LlmState.Loading      => "the Model Server is still loading the model, try again in a moment.",
            LlmState.Unauthorized => "the Model Server rejected the API key.",
            LlmState.Busy         => "the Model Server is busy, try again in a moment.",
            _                     => "the Model Server isn't running.",
        },
    };
}
