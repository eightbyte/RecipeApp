using RecipeApp.API.DTOs;
using RecipeApp.API.DTOs.ModelServer;
using RecipeApp.API.Services.Llm;

namespace RecipeApp.API.Endpoints;

public static class ModelServerEndpoints
{
    public static IEndpointRouteBuilder MapModelServerEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/model-server").WithTags("Model Server");

        // GET /model-server/status
        group.MapGet("/status", async (IModelServerStatusProbe probe, CancellationToken ct) =>
        {
            var status = await probe.GetStatusAsync(ct);
            return Results.Ok(status.ToResponse());
        })
        .Produces<ModelServerStatusResponse>()
        .WithSummary("Whether the Model Server can answer, and the loaded model's name");

        return routes;
    }
}
