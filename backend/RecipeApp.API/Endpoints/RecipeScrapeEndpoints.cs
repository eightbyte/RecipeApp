using FluentValidation;
using RecipeApp.API.DTOs.Scrape;
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
            IValidator<ScrapeRecipeRequest> validator,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

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
        .WithSummary("Scrape a recipe from a URL and return a preview for review");

        // POST /recipes/scrape/confirm
        group.MapPost("/scrape/confirm", async (
            ScrapeConfirmRequest request,
            IRecipeScrapeService svc,
            IValidator<ScrapeConfirmRequest> validator,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var created = await svc.ConfirmAsync(request, ct);
            return Results.Created($"/api/v1/recipes/{created.Id}", created);
        })
        .WithSummary("Confirm a scraped recipe preview and save it to the database");

        return routes;
    }
}
