using System.Text.Json;
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

/// <summary>Each provider's name as the API writes it, the one spelling stored in <c>LlmCalls</c> and accepted as a filter.</summary>
public static class LlmProviderNames
{
    private static readonly Dictionary<LlmProvider, string> ByProvider = Enum.GetValues<LlmProvider>()
        .ToDictionary(provider => provider, provider => JsonSerializer.Serialize(provider).Trim('"'));

    public static IReadOnlyCollection<string> All => ByProvider.Values;

    public static string NameOf(LlmProvider provider) => ByProvider[provider];

    /// <summary>Exact spelling only, so a filter means what the API reports.</summary>
    public static bool TryParse(string? name, out LlmProvider provider)
    {
        foreach (var (candidate, spelling) in ByProvider)
        {
            if (spelling != name) continue;
            provider = candidate;
            return true;
        }

        provider = default;
        return false;
    }

    public static LlmProvider Parse(string name) =>
        TryParse(name, out var provider) ? provider : throw new ArgumentOutOfRangeException(nameof(name), name, null);
}
