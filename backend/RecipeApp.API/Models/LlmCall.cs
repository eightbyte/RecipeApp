using RecipeApp.API.Services.Llm;

namespace RecipeApp.API.Models;

/// <summary>
/// One request that reached an LLM Provider, recorded with its timing, tokens and estimated cost whatever its outcome, and
/// kept forever because it is the only record of what a Scrape cost.
/// </summary>
public class LlmCall
{
    public Guid Id { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public LlmProvider Provider { get; set; }

    /// <summary>The model that answered, or for Claude the configured model when nothing answered; null for a Model Server that named none.</summary>
    public string? Model { get; set; }

    public LlmOperation Operation { get; set; }

    /// <summary>Which call within a Scrape, null for a seed call.</summary>
    public LlmStep? Step { get; set; }

    /// <summary>Shared by every call for the same Scrape, MyPlate recipe or catalogue batch.</summary>
    public Guid CorrelationId { get; set; }

    /// <summary>The Scrape's URL when it has one, never pasted text.</summary>
    public string? SourceUrl { get; set; }

    public LlmCallOutcome Outcome { get; set; }

    /// <summary>Wall clock around the whole call, the SDK's retries and the stream included.</summary>
    public int DurationMs { get; set; }

    /// <summary>Uncached input; null when no usage came back, as from a timeout.</summary>
    public int? InputTokens { get; set; }

    public int? CacheReadTokens { get; set; }

    /// <summary>Null for the Model Server, which has no cache write.</summary>
    public int? CacheWriteTokens { get; set; }

    /// <summary>Thinking included.</summary>
    public int? OutputTokens { get; set; }

    /// <summary>From <c>Llm:Pricing</c> when the row is recorded: 0 for the Model Server, null for a model with no price entry.</summary>
    public decimal? EstimatedCostUsd { get; set; }
}
