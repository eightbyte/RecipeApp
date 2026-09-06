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
}
