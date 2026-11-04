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
/// How the decoder picks among the tokens the grammar allows, left at llama.cpp's own values because lowering the temperature
/// measurably made extraction worse: misreads are systematic, and a warmer retry is what sometimes recovers them.
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
