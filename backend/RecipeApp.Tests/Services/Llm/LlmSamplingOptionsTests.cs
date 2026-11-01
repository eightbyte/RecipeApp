using LLama.Sampling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecipeApp.API.Services.Llm;

namespace RecipeApp.Tests.Services.Llm;

/// <summary>The decoder settings applied to every local model call, asserted against LLamaSharp's own defaults so the test survives the library changing its mind.</summary>
public class LlmSamplingOptionsTests
{
    /// <summary>Asserts the measured outcome rather than the intuitive one: decoding conservatively for extraction measured worse, because the retry that compensates for systematic misreads needs the randomness.</summary>
    [Fact]
    public void Defaults_MatchTheLibrarysPipeline_BecauseDecodingColderMeasuredWorse()
    {
        using var chatTuned = new DefaultSamplingPipeline();
        var ours = new LlmSamplingOptions();

        ours.Temperature.Should().Be(chatTuned.Temperature);
        ours.TopK.Should().Be(chatTuned.TopK);
        ours.TopP.Should().Be(chatTuned.TopP);
        ours.MinP.Should().Be(chatTuned.MinP);
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
                ["Llm:Local:Sampling:Temperature"] = "0.05",
                ["Llm:Local:Sampling:TopK"]        = "12",
                ["Llm:Local:Sampling:TopP"]        = "0.8",
                ["Llm:Local:Sampling:MinP"]        = "0.15",
            })
            .Build();

        var options = new LlmOptions();
        configuration.GetSection(LlmOptions.SectionName).Bind(options);

        options.Local.Sampling.Temperature.Should().Be(0.05f);
        options.Local.Sampling.TopK.Should().Be(12);
        options.Local.Sampling.TopP.Should().Be(0.8f);
        options.Local.Sampling.MinP.Should().Be(0.15f);
    }

    /// <summary>The holder reads its settings before deciding whether it has a model to load, so a machine with no GGUF file still answers the client's question.</summary>
    [Fact]
    public void Holder_SurfacesTheConfiguredSampling_EvenWithNoModelConfigured()
    {
        var options = Options.Create(new LlmOptions
        {
            Local = new LocalLlmOptions
            {
                ModelPath = string.Empty,
                Sampling  = new LlmSamplingOptions { Temperature = 0.3f, TopK = 7 },
            },
        });

        using var holder = new LlamaModelHolder(options, NullLogger<LlamaModelHolder>.Instance);

        holder.IsLoaded.Should().BeFalse();
        holder.Sampling.Temperature.Should().Be(0.3f);
        holder.Sampling.TopK.Should().Be(7);
    }
}
