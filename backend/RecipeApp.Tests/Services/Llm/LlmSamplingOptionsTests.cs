using Microsoft.Extensions.Configuration;
using RecipeApp.API.Services.Llm;

namespace RecipeApp.Tests.Services.Llm;

/// <summary>The decoder settings sent with every Model Server call, pinned to the values the prompts were measured against.</summary>
public class LlmSamplingOptionsTests
{
    /// <summary>Asserts the measured outcome rather than the intuitive one: decoding conservatively for extraction measured worse, because the retry that compensates for systematic misreads needs the randomness.</summary>
    [Fact]
    public void Defaults_AreTheMeasuredValues_BecauseDecodingColderMeasuredWorse()
    {
        var ours = new LlmSamplingOptions();

        ours.Temperature.Should().Be(0.75f);
        ours.TopK.Should().Be(40);
        ours.TopP.Should().Be(0.9f);
        ours.MinP.Should().Be(0.1f);
    }

    /// <summary>Greedy decoding is not available to these pipelines, because at temperature zero a retry reproduces the answer that was just rejected.</summary>
    [Fact]
    public void Defaults_KeepEnoughRandomnessThatARetryIsGenuinelyASecondAttempt()
    {
        new LlmSamplingOptions().Temperature.Should().BeGreaterThan(0f);
    }

    /// <summary>Binding through a real <see cref="ConfigurationBuilder"/>, because the thing that can silently break is a key path.</summary>
    [Fact]
    public void Configuration_BindsEveryDecoderSettingFromTheLlmSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Sampling:Temperature"] = "0.05",
                ["Llm:Sampling:TopK"]        = "12",
                ["Llm:Sampling:TopP"]        = "0.8",
                ["Llm:Sampling:MinP"]        = "0.15",
            })
            .Build();

        var options = new LlmOptions();
        configuration.GetSection(LlmOptions.SectionName).Bind(options);

        options.Sampling.Temperature.Should().Be(0.05f);
        options.Sampling.TopK.Should().Be(12);
        options.Sampling.TopP.Should().Be(0.8f);
        options.Sampling.MinP.Should().Be(0.15f);
    }

    [Fact]
    public void Configuration_BindsTheModelServerSettings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:ModelServer:BaseUrl"]              = "http://host.docker.internal:9931",
                ["Llm:ModelServer:ApiKey"]               = "secret",
                ["Llm:ModelServer:StatusTimeoutSeconds"] = "5",
            })
            .Build();

        var options = new LlmOptions();
        configuration.GetSection(LlmOptions.SectionName).Bind(options);

        options.ModelServer.BaseUrl.Should().Be("http://host.docker.internal:9931");
        options.ModelServer.ApiKey.Should().Be("secret");
        options.ModelServer.StatusTimeoutSeconds.Should().Be(5);
    }

    [Fact]
    public void ModelServerDefaults_MeanNoModelServer_AndAThreeSecondStatusCheck()
    {
        var defaults = new ModelServerOptions();

        defaults.BaseUrl.Should().BeEmpty();
        defaults.ApiKey.Should().BeEmpty();
        defaults.StatusTimeoutSeconds.Should().Be(3);
    }
}
