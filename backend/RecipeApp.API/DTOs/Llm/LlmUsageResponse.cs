using RecipeApp.API.Services.Llm;

namespace RecipeApp.API.DTOs.Llm;

/// <summary>One page of <c>GET /llm/usage</c>, with totals over the whole filtered range rather than just this page.</summary>
/// <param name="TotalCount">Every LLM Call that matches the filters.</param>
public record LlmUsageResponse(
    List<LlmUsageTotalResponse> Totals,
    List<LlmCallResponse> Items,
    int TotalCount
);

/// <summary>The LLM Calls of one provider, model and Operation added up.</summary>
/// <param name="EstimatedCostUsd">The sum over the priced calls only, so one unpriced call never blanks the total.</param>
/// <param name="UnpricedCalls">Calls with no cost, because their model had no price entry, left out of <paramref name="EstimatedCostUsd"/>.</param>
public record LlmUsageTotalResponse(
    LlmProvider Provider,
    string? Model,
    LlmOperation Operation,
    int Calls,
    long InputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    long OutputTokens,
    decimal EstimatedCostUsd,
    int UnpricedCalls
);

public record LlmCallResponse(
    Guid Id,
    DateTime CreatedAt,
    LlmProvider Provider,
    string? Model,
    LlmOperation Operation,
    LlmStep? Step,
    Guid CorrelationId,
    string? SourceUrl,
    LlmCallOutcome Outcome,
    int DurationMs,
    int? InputTokens,
    int? CacheReadTokens,
    int? CacheWriteTokens,
    int? OutputTokens,
    decimal? EstimatedCostUsd
);
