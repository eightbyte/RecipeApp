using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace RecipeApp.API.Services.Llm;

/// <summary>
/// ILlmStructuredClient backed by a local GGUF model via LLamaSharp.
///
/// <para>Decoding is grammar-constrained, which is meant to make schema-valid JSON the only
/// reachable output. Treat that as the intent rather than a guarantee: Phase 9 Stage 4 measured
/// unparseable answers escaping it at a low rate, so the parse failure below is a real path and
/// says what came through.</para>
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

            // The grammar decides which tokens are legal; these decide which legal token is taken.
            // Left at LLamaSharp's chat-tuned defaults, a schema-valid answer can still carry a
            // sampled digit the source line never stated — see LlmSamplingOptions.
            var sampling = holder.Sampling;

            var pipeline = new DefaultSamplingPipeline
            {
                Grammar     = grammar,
                Temperature = sampling.Temperature,
                TopK        = sampling.TopK,
                TopP        = sampling.TopP,
                MinP        = sampling.MinP,
            };

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

            try
            {
                return JsonNode.Parse(json)
                    ?? throw new InvalidOperationException("Model returned empty/invalid JSON.");
            }
            catch (JsonException ex)
            {
                // Constrained decoding is supposed to make this unreachable, so when it happens the
                // grammar has a hole in it and the only thing that can find the hole is the text
                // that got through. Rethrowing the parser's message alone discards that text at the
                // one moment it is worth having: a caller sees "'0' is an invalid end of a number"
                // with no way to learn which number, and cannot reproduce it either, because the
                // answer that provoked it is gone.
                throw new InvalidOperationException(
                    $"Model returned invalid JSON: {ex.Message} Around it: {Excerpt(json, ex)}", ex);
            }
        }
        finally
        {
            holder.Gate.Release();
        }
    }

    /// <summary>
    /// The answer either side of the character the parser objected to, so a failure several
    /// thousand bytes into a decode names the text that caused it rather than only the offset.
    /// </summary>
    internal static string Excerpt(string json, JsonException failure)
    {
        if (json.Length == 0) return "(empty answer)";

        // BytePositionInLine counts bytes and this indexes characters, so it is a starting point
        // rather than an exact hit. Clamped, because the two disagree wherever the answer is not
        // ASCII, and a diagnostic that throws while reporting a failure helps nobody.
        var position = (int)Math.Clamp(failure.BytePositionInLine ?? 0, 0, json.Length - 1);
        var start    = Math.Max(0, position - ExcerptRadius);
        var end      = Math.Min(json.Length, position + ExcerptRadius);

        var excerpt = json[start..end].ReplaceLineEndings(" ");

        return $"…{excerpt}…";
    }

    /// <summary>Characters of context shown either side of the offending character.</summary>
    private const int ExcerptRadius = 60;
}
