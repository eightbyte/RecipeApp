using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.DTOs;
using RecipeApp.API.DTOs.Ingredients;
using RecipeApp.API.Enums;
using RecipeApp.API.Filters;
using RecipeApp.API.Models;
using RecipeApp.API.Services;

namespace RecipeApp.API.Endpoints;

public static class IngredientsEndpoints
{
    public static IEndpointRouteBuilder MapIngredientsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/ingredients").WithTags("Ingredients");

        // GET /ingredients?search=&includeRemoved=
        group.MapGet("/", async (string? search, bool? includeRemoved, AppDbContext db) =>
        {
            var query = db.Ingredients.AsNoTracking().AsQueryable();

            // Pickers never offer a Removed Ingredient; only Admin asks for them.
            if (includeRemoved != true)
                query = query.Where(i => i.RemovedAt == null);

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(i =>
                    EF.Functions.ILike(i.Name, $"%{search.ToLower()}%") ||
                    EF.Functions.ILike(i.DisplayName, $"%{search}%"));

            var ingredients = await query.OrderBy(i => i.DisplayName).ToListAsync();
            return Results.Ok(ingredients.Select(i => i.ToResponse()));
        })
        .WithSummary("List active ingredients (supports ?search= for autocomplete, and ?includeRemoved=true for Removed Ingredients too)");

        // GET /ingredients/categories
        group.MapGet("/categories", () => Results.Ok(IngredientCategory.All))
             .WithSummary("List all valid ingredient categories");

        // GET /ingredients/{id}
        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var i = await db.Ingredients.FindAsync(id);
            return i is null ? Results.NotFound() : Results.Ok(i.ToResponse());
        })
        .WithSummary("Get a single ingredient, removed or not");

        // GET /ingredients/{id}/usage
        group.MapGet("/{id:guid}/usage", async (Guid id, IngredientService ingredients, CancellationToken ct) =>
        {
            var usage = await ingredients.GetUsageAsync(id, ct);
            return usage is null ? Results.NotFound() : Results.Ok(usage);
        })
        .WithSummary("Count the recipes that use an ingredient");

        // DELETE /ingredients/{id}
        group.MapDelete("/{id:guid}", async (Guid id, IngredientService ingredients, CancellationToken ct) =>
            await ingredients.RemoveAsync(id, ct) ? Results.NoContent() : Results.NotFound())
        .WithSummary("Remove an ingredient, which recipes and shopping lists keep showing");

        // POST /ingredients/{id}/restore
        group.MapPost("/{id:guid}/restore", async (Guid id, IngredientService ingredients, CancellationToken ct) =>
            ToHttpResult(await ingredients.RestoreAsync(id, ct)))
        .WithSummary("Restore a removed ingredient, refused when its name or an alias has since been taken");

        // POST /ingredients
        group.MapPost("/", async (
            CreateIngredientRequest request,
            IngredientService ingredients,
            CancellationToken ct) =>
        {
            var result = await ingredients.CreateAsync(request, ct);
            return result.Ingredient is { } ingredient
                ? Results.Created($"/api/v1/ingredients/{ingredient.Id}", ingredient.ToResponse())
                : ToHttpResult(result);
        })
        .WithValidation<CreateIngredientRequest>()
        .WithSummary("Create a new ingredient");

        // PUT /ingredients/{id}
        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateIngredientRequest request,
            IngredientService ingredients,
            CancellationToken ct) =>
        {
            var result = await ingredients.UpdateAsync(id, request, ct);
            return ToHttpResult(result);
        })
        .WithValidation<UpdateIngredientRequest>()
        .WithSummary("Update an ingredient");

        return routes;
    }

    private static IResult ToHttpResult(IngredientEditResult result) => result switch
    {
        { Clash: { } clash }           => Results.Conflict(new { message = clash }),
        { Ingredient: { } ingredient } => Results.Ok(ingredient.ToResponse()),
        _                              => Results.NotFound(),
    };
}
