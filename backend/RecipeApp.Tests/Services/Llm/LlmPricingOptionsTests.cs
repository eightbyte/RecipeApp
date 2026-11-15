using Microsoft.Extensions.Configuration;
using RecipeApp.API.Services.Llm;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Services.Llm;

/// <summary>The hand-kept price table in the committed <c>appsettings.json</c>, which costs every Claude call.</summary>
public class LlmPricingOptionsTests
{
    private static LlmOptions CommittedOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepositoryPaths.ApiContentRoot, "appsettings.json"))
            .Build();

        var options = new LlmOptions();
        configuration.GetSection(LlmOptions.SectionName).Bind(options);
        return options;
    }

    /// <summary>Changing <c>Llm:Anthropic:Model</c> without adding its price would make every Scrape's cost null.</summary>
    [Fact]
    public void TheConfiguredClaudeModel_HasAPriceEntry()
    {
        var options = CommittedOptions();

        options.Pricing.Should().ContainKey(options.Anthropic.Model);
    }

    /// <summary>Anthropic's list prices as of 2026-11, bound as decimals so a cost never picks up a binary rounding error.</summary>
    [Fact]
    public void TheDefaultEntry_IsAnthropicsListPrice_PerMillionTokens()
    {
        CommittedOptions().Pricing["claude-opus-5-5"].Should().BeEquivalentTo(new LlmModelPrice
        {
            Input = 4.00m, Output = 20.00m, CacheRead = 0.20m, CacheWrite = 5.00m,
        });
    }
}
