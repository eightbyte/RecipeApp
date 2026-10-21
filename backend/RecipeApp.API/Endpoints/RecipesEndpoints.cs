using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.DTOs.Recipes;
using RecipeApp.API.Filters;
using RecipeApp.API.Services;

namespace RecipeApp.API.Endpoints;

public static class RecipesEndpoints
{
    public static IEndpointRouteBuilder MapRecipesEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/recipes").WithTags("Recipes");

        // GET /recipes
        group.MapGet("/", async (
            string? search,
            string? ingredient,
            int? excludeRecentDays,
            DateTime? lastCookedBefore,
            RecipeService svc) =>
            Results.Ok(await svc.GetListAsync(search, ingredient, excludeRecentDays, lastCookedBefore))
        )
        .WithSummary("List recipes")
        .WithDescription("Supports optional filters: search (name), ingredient, excludeRecentDays, lastCookedBefore.");

        // GET /recipes/{id}
        group.MapGet("/{id:guid}", async (Guid id, RecipeService svc) =>
        {
            var recipe = await svc.GetByIdAsync(id);
            return recipe is null ? Results.NotFound() : Results.Ok(recipe);
        })
        .WithSummary("Get a single recipe with full ingredient list and steps");

        // POST /recipes
        group.MapPost("/", async (
            CreateRecipeRequest request,
            RecipeService svc) =>
        {
            var created = await svc.CreateAsync(request);
            return Results.Created($"/api/v1/recipes/{created.Id}", created);
        })
        .WithValidation<CreateRecipeRequest>()
        .WithSummary("Create a new recipe");

        // PUT /recipes/{id}
        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateRecipeRequest request,
            RecipeService svc) =>
        {
            var updated = await svc.UpdateAsync(id, request);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .WithValidation<UpdateRecipeRequest>()
        .WithSummary("Replace a recipe's ingredients and steps");

        // DELETE /recipes/{id}
        group.MapDelete("/{id:guid}", async (Guid id, RecipeService svc) =>
        {
            var deleted = await svc.DeleteAsync(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithSummary("Delete a recipe");

        // POST /recipes/{id}/image
        group.MapPost("/{id:guid}/image", async (
            Guid id,
            IFormFile file,
            AppDbContext db,
            ImageService imageSvc) =>
        {
            var recipe = await db.Recipes.FindAsync(id);
            if (recipe is null) return Results.NotFound();

            var (ok, error) = imageSvc.Validate(file);
            if (!ok) return Results.BadRequest(new { message = error });

            // Remove previous image
            if (recipe.ImageUrl is not null) imageSvc.Delete(recipe.ImageUrl);

            recipe.ImageUrl  = await imageSvc.SaveAsync(file);
            recipe.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(new { imageUrl = recipe.ImageUrl });
        })
        .WithSummary("Upload or replace the recipe image")
        .DisableAntiforgery();

        // POST /recipes/{id}/cook — mark as cooked (updates LastCookedAt)
        group.MapPost("/{id:guid}/cook", async (Guid id, RecipeService svc) =>
        {
            var updated = await svc.MarkCookedAsync(id);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .WithSummary("Mark recipe as cooked (updates LastCookedAt to now)");

        return routes;
    }
}
