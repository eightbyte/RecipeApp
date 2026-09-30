using LLama.Sampling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecipeApp.API.Services.Llm;

namespace RecipeApp.Tests.Services.Llm;

/// <summary>
/// The decoder settings applied to every local model call.
///
/// <para>These are asserted against LLamaSharp's own defaults rather than against numbers copied
/// into the test, so the claim under test is the one that matters — that this application decodes
/// more conservatively than a chat client — and it survives the library changing its mind.</para>
/// </summary>
public class LlmSamplingOptionsTests
{
    /// <summary>
    /// Asserts the measured outcome, not the intuitive one. Decoding conservatively for an
    /// extraction task reads as obviously right and measured worse, because the quantity misreads it
    /// was meant to fix are systematic and the retry that was compensating for them needs the
    /// randomness. This test exists so that reasoning has to be redone rather than assumed.
    /// </summary>
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

    /// <summary>
    /// Whatever the value, greedy decoding is not available to these pipelines: a rejected recipe is
    /// retried, and at temperature zero the retry reproduces the answer that was just rejected.
    /// </summary>
    [Fact]
    public void Defaults_KeepEnoughRandomnessThatARetryIsGenuinelyASecondAttempt()
    {
        new LlmSamplingOptions().Temperature.Should().BeGreaterThan(0f);
    }

    /// <summary>
    /// Binding through a real <see cref="ConfigurationBuilder"/>, because the thing that can silently
    /// break is a key path, and a hand-built options object would never exercise one.
    /// </summary>
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

    /// <summary>
    /// The holder reads its settings before it decides whether it has a model to load, so a machine
    /// with no GGUF file still answers the question the client asks it. Without that ordering the
    /// client would dereference an unset property on exactly the machines that run the test suite.
    /// </summary>
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
