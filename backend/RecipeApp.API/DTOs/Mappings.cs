using RecipeApp.API.DTOs.Ingredients;
using RecipeApp.API.DTOs.Recipes;
using RecipeApp.API.Models;

namespace RecipeApp.API.DTOs;

/// <summary>
/// Static mapping helpers. Pure functions: entity in, DTO out.
/// No reflection, no magic — straightforward and easy to trace.
/// </summary>
public static class Mappings
{
    // ── Ingredient ────────────────────────────────────────────────────────────

    public static IngredientResponse ToResponse(this Ingredient i) => new(
        i.Id,
        i.Name,
        i.DisplayName,
        i.Category,
        i.DefaultUnit,
        i.CreatedAt
    );

    // ── Recipe ────────────────────────────────────────────────────────────────

    public static RecipeListItemResponse ToListItem(this Recipe r) => new(
        r.Id,
        r.Name,
        r.Description,
        r.ImageUrl,
        r.Servings,
        r.Ingredients.Count,
        r.LastCookedAt,
        r.CreatedAt
    );

    public static RecipeDetailResponse ToDetail(this Recipe r) => new(
        r.Id,
        r.Name,
        r.Description,
        r.ImageUrl,
        r.SourceUrl,
        r.Servings,
        r.LastCookedAt,
        r.CreatedAt,
        r.UpdatedAt,
        r.Ingredients
            .OrderBy(i => i.DisplayOrder)
            .Select(ToResponse)
            .ToList(),
        r.Steps
            .OrderBy(s => s.StepNumber)
            .Select(ToResponse)
            .ToList()
    );

    public static RecipeIngredientResponse ToResponse(this RecipeIngredient ri) => new(
        ri.Id,
        ri.IngredientId,
        ri.Ingredient.Name,
        ri.Ingredient.DisplayName,
        ri.Ingredient.Category,
        ri.Amount,
        ri.Unit,
        ri.Notes,
        ri.DisplayOrder
    );

    public static RecipeStepResponse ToResponse(this RecipeStep rs) => new(
        rs.Id,
        rs.StepNumber,
        rs.Instruction,
        rs.StepIngredients.Select(si => si.RecipeIngredientId).ToList()
    );
}
