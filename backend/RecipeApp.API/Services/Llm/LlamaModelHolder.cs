using LLama;
using LLama.Common;
using Microsoft.Extensions.Options;

namespace RecipeApp.API.Services.Llm;

/// <summary>
/// Owns the single loaded model and a concurrency gate. Registered as a singleton.
/// Weights are null when ModelPath is not configured.
/// </summary>
public sealed class LlamaModelHolder : IDisposable
{
    public LLamaWeights? Weights { get; }
    public ModelParams? Parameters { get; }
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public string[] StopStrings { get; }

    private readonly LocalLlmOptions _local;

    public bool IsLoaded => Weights != null;

    public LlamaModelHolder(IOptions<LlmOptions> options, ILogger<LlamaModelHolder> logger)
    {
        _local     = options.Value.Local;
        StopStrings = _local.StopStrings;

        if (string.IsNullOrWhiteSpace(_local.ModelPath))
        {
            logger.LogWarning(
                "LLM:Local:ModelPath is not configured. Local inference will not be available.");
            return;
        }

        Parameters = new ModelParams(_local.ModelPath)
        {
            ContextSize   = (uint)_local.ContextSize,
            GpuLayerCount = _local.GpuLayerCount,
        };

        logger.LogInformation("Loading LLM model from {Path}…", _local.ModelPath);
        Weights = LLamaWeights.LoadFromFile(Parameters);
        logger.LogInformation("LLM model loaded successfully.");
    }

    public string BuildChatPrompt(string system, string user) =>
        _local.ChatTemplate?.ToLowerInvariant() switch
        {
            "llama3" =>
                $"<|begin_of_text|><|start_header_id|>system<|end_header_id|>\n\n{system}" +
                $"<|eot_id|><|start_header_id|>user<|end_header_id|>\n\n{user}" +
                $"<|eot_id|><|start_header_id|>assistant<|end_header_id|>\n\n",

            _ => // ChatML — default (Qwen, Mistral, etc.)
                $"<|im_start|>system\n{system}<|im_end|>\n" +
                $"<|im_start|>user\n{user}<|im_end|>\n" +
                $"<|im_start|>assistant\n",
        };

    public void Dispose()
    {
        Weights?.Dispose();
        Gate.Dispose();
    }
}
