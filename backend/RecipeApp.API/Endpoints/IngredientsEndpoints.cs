using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.DTOs;
using RecipeApp.API.DTOs.Ingredients;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;

namespace RecipeApp.API.Endpoints;

public static class IngredientsEndpoints
{
    public static IEndpointRouteBuilder MapIngredientsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/ingredients").WithTags("Ingredients");

        // GET /ingredients?search=
        group.MapGet("/", async (string? search, AppDbContext db) =>
        {
            var query = db.Ingredients.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(i =>
                    EF.Functions.ILike(i.Name, $"%{search.ToLower()}%") ||
                    EF.Functions.ILike(i.DisplayName, $"%{search}%"));

            var ingredients = await query.OrderBy(i => i.DisplayName).ToListAsync();
            return Results.Ok(ingredients.Select(i => i.ToResponse()));
        })
        .WithSummary("List all ingredients (supports ?search= for autocomplete)");

        // GET /ingredients/categories
        group.MapGet("/categories", () => Results.Ok(IngredientCategory.All))
             .WithSummary("List all valid ingredient categories");

        // GET /ingredients/{id}
        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var i = await db.Ingredients.FindAsync(id);
            return i is null ? Results.NotFound() : Results.Ok(i.ToResponse());
        })
        .WithSummary("Get a single ingredient");

        // POST /ingredients
        group.MapPost("/", async (
            CreateIngredientRequest request,
            AppDbContext db,
            IValidator<CreateIngredientRequest> validator) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            if (await db.Ingredients.AnyAsync(i => i.Name == request.Name))
                return Results.Conflict(new { message = $"Ingredient '{request.Name}' already exists." });

            var ingredient = new Ingredient
            {
                Id          = Guid.NewGuid(),
                Name        = request.Name,
                DisplayName = request.DisplayName,
                Category    = request.Category,
                DefaultUnit = request.DefaultUnit,
                CreatedAt   = DateTime.UtcNow,
            };

            db.Ingredients.Add(ingredient);
            await db.SaveChangesAsync();

            return Results.Created($"/api/v1/ingredients/{ingredient.Id}", ingredient.ToResponse());
        })
        .WithSummary("Create a new ingredient");

        // PUT /ingredients/{id}
        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateIngredientRequest request,
            AppDbContext db,
            IValidator<UpdateIngredientRequest> validator) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var ingredient = await db.Ingredients.FindAsync(id);
            if (ingredient is null) return Results.NotFound();

            ingredient.DisplayName = request.DisplayName;
            ingredient.Category    = request.Category;
            ingredient.DefaultUnit = request.DefaultUnit;
            await db.SaveChangesAsync();

            return Results.Ok(ingredient.ToResponse());
        })
        .WithSummary("Update an ingredient");

        return routes;
    }
}
