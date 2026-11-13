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

                return ToProblem(ex);
            }
        })
        .WithValidation<ScrapeRecipeRequest>()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .WithSummary("Scrape a recipe from a URL and return a preview for review");

        // POST /recipes/scrape/text
        group.MapPost("/scrape/text", async (
            ScrapeTextRequest request,
            IRecipeScrapeService svc,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            try
            {
                var preview = await svc.ScrapeTextAsync(request.Text, ct);
                return Results.Ok(preview);
            }
            catch (RecipeScrapeException ex)
            {
                logger.LogWarning("Scrape of pasted text ({Length} characters) failed: [{Error}] {Message}",
                    request.Text.Length, ex.Error, ex.Message);

                return ToProblem(ex);
            }
        })
        .WithValidation<ScrapeTextRequest>()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .WithSummary("Scrape a recipe from pasted text and return a preview for review");

        // POST /recipes/scrape/confirm
        group.MapPost("/scrape/confirm", async (
            ScrapeConfirmRequest request,
            IRecipeScrapeService svc,
            CancellationToken ct) =>
        {
            var created = await svc.ConfirmAsync(request, ct);
            return Results.Created($"/api/v1/recipes/{created.Recipe.Id}", created);
        })
        .WithValidation<ScrapeConfirmRequest>()
        .Produces<ScrapeConfirmResponse>(StatusCodes.Status201Created)
        .WithSummary("Confirm a scraped recipe preview and save it to the database");

        return routes;
    }

    /// <summary>Every failed Scrape is the source's fault (422) except an unavailable Model Server, which a retry later can fix (503).</summary>
    private static IResult ToProblem(RecipeScrapeException ex) =>
        Results.Problem(
            detail: ex.Message,
            statusCode: ex.Error == RecipeScrapeError.ModelServerUnavailable
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status422UnprocessableEntity);
}
