using System.Text.Json.Serialization;

namespace RecipeApp.API.Services.Llm;

/// <summary>Who answers Scraping's completions, chosen per deployment by <c>Llm:Provider</c> and fixed until the app restarts.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LlmProvider>))]
public enum LlmProvider
{
    /// <summary>llama.cpp's <c>llama-server</c> on this network, which the seed pipeline always uses.</summary>
    [JsonStringEnumMemberName("modelServer")]
    ModelServer,

    /// <summary>The Claude API, a cloud provider, so a Scrape's content leaves the network.</summary>
    [JsonStringEnumMemberName("anthropic")]
    Anthropic,
}
