using Microsoft.Extensions.Options;

namespace RecipeApp.API.Services.Llm;

public static class LlmServiceCollectionExtensions
{
    /// <summary>
    /// Registers every provider's client and status probe keyed by <see cref="LlmProvider"/>, and the unkeyed ones as the
    /// provider <c>Llm:Provider</c> names, which is what Scraping takes; the seed pipeline asks for the Model Server by key.
    /// </summary>
    public static IServiceCollection AddLlm(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));

        // No timeout: each caller's LlmTimeoutSeconds budget owns the limit, so a long completion surfaces as LlmTimeout rather than a failure.
        services.AddHttpClient(ModelServerClient.HttpClientName, client =>
            client.Timeout = Timeout.InfiniteTimeSpan);

        // No timeout either: Llm:Anthropic:TimeoutSeconds owns it, and HttpClient's own 100 s default would cut it short.
        services.AddHttpClient(AnthropicStructuredClient.HttpClientName, client =>
            client.Timeout = Timeout.InfiniteTimeSpan);

        services.AddKeyedSingleton<ILlmStructuredClient, ModelServerClient>(LlmProvider.ModelServer);
        services.AddKeyedSingleton<ILlmStatusProbe, ModelServerStatusProbe>(LlmProvider.ModelServer);
        services.AddKeyedSingleton<ILlmStructuredClient, AnthropicStructuredClient>(LlmProvider.Anthropic);
        services.AddKeyedSingleton<ILlmStatusProbe, AnthropicStatusProbe>(LlmProvider.Anthropic);

        services.AddSingleton(sp => sp.GetRequiredKeyedService<ILlmStructuredClient>(ConfiguredProvider(sp)));
        services.AddSingleton(sp => sp.GetRequiredKeyedService<ILlmStatusProbe>(ConfiguredProvider(sp)));

        return services;
    }

    private static LlmProvider ConfiguredProvider(IServiceProvider sp) =>
        sp.GetRequiredService<IOptions<LlmOptions>>().Value.Provider;
}
