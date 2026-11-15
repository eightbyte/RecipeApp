using RecipeApp.API.DTOs;
using RecipeApp.API.DTOs.Llm;
using RecipeApp.API.Filters;
using RecipeApp.API.Services.Llm;
using RecipeApp.API.Validators;

namespace RecipeApp.API.Endpoints;

public static class LlmEndpoints
{
    public static IEndpointRouteBuilder MapLlmEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/llm").WithTags("LLM");

        // GET /llm/status
        group.MapGet("/status", async (ILlmStatusProbe probe, CancellationToken ct) =>
        {
            var status = await probe.GetStatusAsync(ct);
            return Results.Ok(status.ToResponse());
        })
        .Produces<LlmStatusResponse>()
        .WithSummary("Which LLM provider Scraping uses, whether it can answer, and the model's name");

        // GET /llm/usage
        group.MapGet("/usage", async ([AsParameters] LlmUsageQuery query, LlmUsageService usage, CancellationToken ct) =>
            Results.Ok(await usage.GetAsync(query, ct))
        )
        .WithValidation<LlmUsageQuery>()
        .Produces<LlmUsageResponse>()
        .WithSummary("Recorded LLM Calls, with their tokens and estimated cost")
        .WithDescription(
            $"Covers from (inclusive, default {LlmUsagePaging.DefaultDays} days ago) to to (exclusive, default now), in UTC. " +
            $"Filters by operation ({string.Join(", ", Enum.GetNames<LlmOperation>())}) and provider " +
            $"({string.Join(", ", LlmProviderNames.All)}). Items are newest first; page counts from 1 (default 1), and pageSize " +
            $"defaults to {LlmUsagePaging.DefaultPageSize} and is at most {LlmUsagePaging.MaxPageSize}. totals cover the whole " +
            "range, grouped by provider, model and operation; estimatedCostUsd sums the priced calls, and unpricedCalls counts " +
            "those whose model has no price entry.");

        return routes;
    }
}
