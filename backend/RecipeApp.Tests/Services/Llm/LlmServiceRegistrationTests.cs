using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RecipeApp.API.Services;
using RecipeApp.API.Services.Llm;
using RecipeApp.API.Services.Seeding;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Services.Llm;

/// <summary>Only Scraping follows <c>Llm:Provider</c>; the seed pipeline was measured against the Model Server and always uses it.</summary>
public class LlmServiceRegistrationTests
{
    private static ServiceProvider Build(string? provider, Action<IServiceCollection>? configure = null)
    {
        var settings = new Dictionary<string, string?>();
        if (provider is not null) settings["Llm:Provider"] = provider;

        var services = new ServiceCollection()
            .AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>))
            .AddLlm(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    [Fact]
    public void WithNoProviderSet_ScrapingUsesTheModelServer()
    {
        using var services = Build(provider: null);

        services.GetRequiredService<ILlmStructuredClient>().Should().BeOfType<ModelServerClient>();
        services.GetRequiredService<ILlmStatusProbe>().Should().BeOfType<ModelServerStatusProbe>();
    }

    /// <summary>The value is spelt as an <c>LLM_PROVIDER</c> line in <c>.env</c> would spell it.</summary>
    [Fact]
    public void WithProviderAnthropic_ScrapingUsesClaude()
    {
        using var services = Build("Anthropic");

        services.GetRequiredService<ILlmStructuredClient>().Should().BeOfType<AnthropicStructuredClient>();
        services.GetRequiredService<ILlmStatusProbe>().Should().BeOfType<AnthropicStatusProbe>();
    }

    [Fact]
    public async Task WithProviderAnthropic_TheSeedNormaliserStillCallsTheModelServer()
    {
        var modelServer = new StubLlmStructuredClient(_ =>
            throw new LlmUnavailableException(LlmProvider.ModelServer, LlmState.Offline));

        using var services = Build("Anthropic", collection => collection
            .Configure<RecipeSeedingOptions>(_ => { })
            .AddKeyedSingleton<ILlmStructuredClient>(LlmProvider.ModelServer, modelServer)
            .AddSingleton<SeedRecipeNormaliser>());

        var act = () => services.GetRequiredService<SeedRecipeNormaliser>().NormaliseAsync(ParsedRecipe(), "0f1e2d3c");

        (await act.Should().ThrowAsync<LlmUnavailableException>()).Which.Provider.Should().Be(LlmProvider.ModelServer);
        modelServer.CallCount.Should().Be(1);
    }

    [Fact]
    public void WithProviderAnthropic_TheCatalogueBuilderResolves()
    {
        using var services = Build("Anthropic", collection => collection
            .Configure<RecipeSeedingOptions>(_ => { })
            .AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new TestHostEnvironment(Path.GetTempPath()))
            .AddSingleton<SeedCacheStore>()
            .AddSingleton<CatalogueBuildFileStore>()
            .AddSingleton<IngredientCatalogueBuilder>());

        services.GetRequiredService<IngredientCatalogueBuilder>().Should().NotBeNull();
    }

    private static ParsedSeedRecipe ParsedRecipe() => new()
    {
        Slug         = "20-minute-chicken-creole",
        SourceUrl    = "https://www.myplate.gov/recipes/20-minute-chicken-creole",
        Template     = MyPlateTemplate.Primary,
        Name         = "20-Minute Chicken Creole",
        Description  = "A Creole-inspired dish.",
        Servings     = 8,
        ImageUrl     = "https://example.test/creole.jpg",
        SourceYield  = "8 servings",
        Notes        = null,
        SourceCredit = "Recipe Adapted from: Food Hero",
        Ingredients  = [new ParsedIngredientLine("1 pound skinless chicken breasts", null)],
        Steps        = ["Cook the chicken."],
    };
}
