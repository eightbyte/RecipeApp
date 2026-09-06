using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace RecipeApp.API.Services.Llm;

/// <summary>
/// ILlmStructuredClient backed by a local GGUF model via LLamaSharp.
/// Grammar-constrained decoding guarantees schema-valid JSON output.
/// </summary>
public sealed class LLamaSharpStructuredClient(LlamaModelHolder holder) : ILlmStructuredClient
{
    public async Task<JsonNode> CompleteStructuredAsync(
        string systemPrompt,
        string userContent,
        JsonNode jsonSchema,
        int maxTokens,
        CancellationToken ct = default)
    {
        if (!holder.IsLoaded)
            throw new InvalidOperationException(
                "Local LLM model is not loaded. Set Llm:Local:ModelPath in configuration.");

        var gbnf     = JsonSchemaGrammar.ToGbnf(jsonSchema);
        var grammar  = new Grammar(gbnf, "root");
        var prompt   = holder.BuildChatPrompt(systemPrompt, userContent);

        // A llama.cpp context is not safe for concurrent decode — serialize calls.
        await holder.Gate.WaitAsync(ct);
        try
        {
            var executor = new StatelessExecutor(holder.Weights!, holder.Parameters!);

            var pipeline = new DefaultSamplingPipeline { Grammar = grammar };

            var inference = new InferenceParams
            {
                MaxTokens        = maxTokens,
                SamplingPipeline = pipeline,
                AntiPrompts      = holder.StopStrings,
            };

            var sb = new StringBuilder();
            await foreach (var token in executor.InferAsync(prompt, inference, ct))
                sb.Append(token);

            var json = sb.ToString().Trim();
            return JsonNode.Parse(json)
                ?? throw new InvalidOperationException("Model returned empty/invalid JSON.");
        }
        finally
        {
            holder.Gate.Release();
        }
    }
}
