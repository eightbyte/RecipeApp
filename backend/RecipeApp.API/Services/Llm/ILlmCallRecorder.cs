using RecipeApp.API.Models;

namespace RecipeApp.API.Services.Llm;

/// <summary>Stores LLM Calls, apart from any caller's unit of work, so a rolled-back caller never drops a row.</summary>
public interface ILlmCallRecorder
{
    Task RecordAsync(LlmCall call, CancellationToken ct = default);
}
