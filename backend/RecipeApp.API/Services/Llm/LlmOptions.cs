namespace RecipeApp.API.Services.Llm;

public class LlmOptions
{
    public const string SectionName = "Llm";

    public string Provider { get; init; } = "Local";
    public LocalLlmOptions Local { get; init; } = new();
}

public class LocalLlmOptions
{
    public string ModelPath { get; init; } = string.Empty;
    public int ContextSize { get; init; } = 8192;
    public int GpuLayerCount { get; init; } = 999;
    public string ChatTemplate { get; init; } = "chatml";
    public string? EmbeddingModelPath { get; init; }
    public string[] StopStrings { get; init; } = [];
    public LlmSamplingOptions Sampling { get; init; } = new();
}

/// <summary>
/// How the decoder picks among the tokens the grammar allows. Exists because the question "should
/// extraction decode more conservatively than chat?" was worth asking, and because the answer turned
/// out to be interesting enough to be worth recording where the setting lives.
///
/// <para><b>The values are llama.cpp's own, and that is a measured result rather than an
/// oversight.</b> Every call this application makes is an extraction, so lowering the temperature
/// looks obviously right: the grammar fixes the shape of the answer but says nothing about which
/// number appears in it, and Phase 9 Stage 4 was losing recipes to quantities the source line never
/// stated. Dropping to 0.2 made that <i>worse</i> — 6 rejections in 45 recipes became 9 in 44.</para>
///
/// <para><b>The reason is the retry.</b> Those misreads are systematic, not noisy: they are the
/// model's considered answer, and they reproduce. A rejected recipe is retried
/// (<c>RecipeSeeding:MaxLlmRetries</c>), and at chat temperature a retry sometimes samples the
/// correct digit and recovers the recipe. At 0.2 it does not — two attempts on the same recipe were
/// observed returning byte-identical output, which spends the retry budget re-deriving a known
/// failure. Temperature is the wrong lever for a systematic error, and lowering it also removes the
/// only thing that was compensating for one.</para>
///
/// <para>Left configurable so the experiment can be repeated against a different model, where the
/// balance may fall the other way.</para>
/// </summary>
public class LlmSamplingOptions
{
    /// <summary>Flattening applied before sampling. Lower is more deterministic.</summary>
    public float Temperature { get; init; } = 0.75f;

    /// <summary>Keep only this many highest-probability tokens.</summary>
    public int TopK { get; init; } = 40;

    /// <summary>Keep the smallest set of tokens whose probabilities sum to this.</summary>
    public float TopP { get; init; } = 0.9f;

    /// <summary>Discard tokens below this fraction of the most likely token's probability.</summary>
    public float MinP { get; init; } = 0.1f;
}
