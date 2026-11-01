using RecipeApp.API.DTOs.Ingredients;
using RecipeApp.API.DTOs.MealPlans;
using RecipeApp.API.DTOs.Recipes;
using RecipeApp.API.DTOs.ShoppingLists;
using RecipeApp.API.Models;

namespace RecipeApp.API.DTOs;

/// <summary>Static mapping helpers: pure functions from entity to DTO, with no reflection.</summary>
public static class Mappings
{
    // ── Ingredient ────────────────────────────────────────────────────────────

    public static IngredientResponse ToResponse(this Ingredient i) => new(
        i.Id,
        i.Name,
        i.DisplayName,
        i.Category,
        i.DefaultUnit,
        i.GramsPerMillilitre,
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
        ri.SourceAmount,
        ri.SourceUnit,
        ri.Notes,
        ri.DisplayOrder
    );

    public static RecipeStepResponse ToResponse(this RecipeStep rs) => new(
        rs.Id,
        rs.StepNumber,
        rs.Instruction,
        rs.StepIngredients.Select(si => si.RecipeIngredientId).ToList()
    );

    // ── MealPlan ──────────────────────────────────────────────────────────────

    public static MealPlanListItemResponse ToListItem(this MealPlan p) => new(
        p.Id,
        p.Name,
        p.IsActive,
        p.PlannedMeals.Count,
        p.PlannedMeals.Where(meal => meal.ScheduledDate.HasValue).Min(meal => (DateOnly?)meal.ScheduledDate),
        p.PlannedMeals.Where(meal => meal.ScheduledDate.HasValue).Max(meal => (DateOnly?)meal.ScheduledDate),
        p.CreatedAt,
        p.ClosedAt
    );

    public static MealPlanDetailResponse ToDetail(this MealPlan p) => new(
        p.Id,
        p.Name,
        p.IsActive,
        p.CreatedAt,
        p.ClosedAt,
        p.PlannedMeals
            .OrderBy(r => r.ScheduledDate.HasValue ? 0 : 1)
            .ThenBy(r => r.ScheduledDate)
            .ThenBy(r => r.DisplayOrder)
            .Select(ToResponse)
            .ToList()
    );

    public static PlannedMealResponse ToResponse(this PlannedMeal plannedMeal) => new(
        plannedMeal.Id,
        plannedMeal.RecipeId,
        plannedMeal.Recipe.Name,
        plannedMeal.Recipe.ImageUrl,
        plannedMeal.Recipe.LastCookedAt,
        plannedMeal.ScheduledDate,
        plannedMeal.BatchSize,
        plannedMeal.DisplayOrder
    );

    // ── ShoppingList ──────────────────────────────────────────────────────────

    public static ShoppingListItemResponse ToResponse(this ShoppingListItem i) => new(
        i.Id,
        i.IngredientId,
        i.IsCustom ? (i.CustomName ?? string.Empty) : i.Ingredient!.DisplayName,
        i.Category,
        i.Amount,
        i.Unit,
        i.IsChecked,
        i.IsCustom,
        i.NeedsReview,
        i.DisplayOrder
    );

    public static ShoppingListResponse ToResponse(this ShoppingList s, bool isStale) => new(
        s.Id,
        s.MealPlanId,
        s.MealPlan.Name,
        s.GeneratedAt,
        isStale,
        s.Items
            .OrderBy(i => i.DisplayOrder)
            .Select(ToResponse)
            .ToList()
    );
}
