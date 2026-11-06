using System.Text.Json.Serialization;

namespace RecipeApp.API.Services.Llm;

/// <summary>Whether the Model Server can answer, as the status endpoint reports it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ModelServerState>))]
public enum ModelServerState
{
    /// <summary>Unreachable, or no <see cref="ModelServerOptions.BaseUrl"/> is configured.</summary>
    [JsonStringEnumMemberName("offline")]
    Offline,

    /// <summary>Running, but still loading the model.</summary>
    [JsonStringEnumMemberName("loading")]
    Loading,

    [JsonStringEnumMemberName("ready")]
    Ready,

    /// <summary>Running, but it rejects the configured API key.</summary>
    [JsonStringEnumMemberName("unauthorized")]
    Unauthorized,
}

public static class ModelServerStateExtensions
{
    /// <summary>Why inference is unavailable in this state, a lower-case clause that follows "… is unavailable: " so every caller, and the frontend, words it the same way.</summary>
    public static string UnavailableReason(this ModelServerState state) => state switch
    {
        ModelServerState.Loading      => "the Model Server is still loading the model, try again in a moment.",
        ModelServerState.Unauthorized => "the Model Server rejected the API key.",
        _                             => "the Model Server isn't running.",
    };
}
