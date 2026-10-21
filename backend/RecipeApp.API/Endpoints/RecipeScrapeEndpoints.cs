using RecipeApp.API.DTOs.Scrape;
using RecipeApp.API.Filters;
using RecipeApp.API.Services;

namespace RecipeApp.API.Endpoints;

public static class RecipeScrapeEndpoints
{
    public static IEndpointRouteBuilder MapRecipeScrapeEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/recipes").WithTags("Recipes");

        // POST /recipes/scrape
        group.MapPost("/scrape", async (
            ScrapeRecipeRequest request,
            IRecipeScrapeService svc,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            try
            {
                var preview = await svc.ScrapeAsync(request.Url, ct);
                return Results.Ok(preview);
            }
            catch (RecipeScrapeException ex)
            {
                logger.LogWarning("Scrape failed for URL {Url}: [{Error}] {Message}",
                    request.Url, ex.Error, ex.Message);

                return ex.Error switch
                {
                    RecipeScrapeError.FetchTimeout or
                    RecipeScrapeError.FetchFailed or
                    RecipeScrapeError.FetchNonSuccess or
                    RecipeScrapeError.LlmTimeout or
                    RecipeScrapeError.LlmFailed or
                    RecipeScrapeError.NoContent =>
                        Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity),
                    _ => Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity),
                };
            }
        })
        .WithValidation<ScrapeRecipeRequest>()
        .WithSummary("Scrape a recipe from a URL and return a preview for review");

        // POST /recipes/scrape/confirm
        group.MapPost("/scrape/confirm", async (
            ScrapeConfirmRequest request,
            IRecipeScrapeService svc,
            CancellationToken ct) =>
        {
            var created = await svc.ConfirmAsync(request, ct);
            return Results.Created($"/api/v1/recipes/{created.Id}", created);
        })
        .WithValidation<ScrapeConfirmRequest>()
        .WithSummary("Confirm a scraped recipe preview and save it to the database");

        return routes;
    }
}
