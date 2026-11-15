using RecipeApp.API.DTOs;
using RecipeApp.API.DTOs.Llm;
using RecipeApp.API.Services.Llm;

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

        return routes;
    }
}
