using RecipeApp.API.DTOs.MealPlans;
using RecipeApp.API.DTOs.Recipes;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;

namespace RecipeApp.Tests.Helpers;

public static class TestDataBuilder
{
    public static Ingredient Ingredient(
        string name = "test-ingredient",
        string displayName = "Test Ingredient",
        string category = IngredientCategory.Other,
        string? defaultUnit = "g",
        decimal? gramsPerMillilitre = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        DisplayName = displayName,
        Category = category,
        DefaultUnit = defaultUnit,
        GramsPerMillilitre = gramsPerMillilitre,
        CreatedAt = DateTime.UtcNow,
    };

    public static Recipe Recipe(
        string name = "Test Recipe",
        int servings = 4,
        DateTime? lastCookedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Servings = servings,
        LastCookedAt = lastCookedAt,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    public static CreateRecipeRequest CreateRecipeRequest(
        Guid ingredientId,
        string name = "Test Recipe",
        int servings = 4,
        int ingredientCount = 1,
        int stepCount = 1) => new(
        name,
        null,
        null,
        servings,
        Enumerable.Range(0, ingredientCount)
            .Select(i => new RecipeIngredientRequest(ingredientId, 100m, "g", null, i))
            .ToList(),
        Enumerable.Range(0, stepCount)
            .Select(i => new RecipeStepRequest(i + 1, $"Step {i + 1}", []))
            .ToList()
    );

    public static UpdateRecipeRequest UpdateRecipeRequest(
        Guid ingredientId,
        string name = "Updated Recipe",
        int servings = 2,
        int ingredientCount = 1,
        int stepCount = 1) => new(
        name,
        null,
        null,
        servings,
        Enumerable.Range(0, ingredientCount)
            .Select(i => new RecipeIngredientRequest(ingredientId, 50m, "g", null, i))
            .ToList(),
        Enumerable.Range(0, stepCount)
            .Select(i => new RecipeStepRequest(i + 1, $"Updated step {i + 1}", []))
            .ToList()
    );

    public static MealPlan MealPlan(
        string name = "Test Plan",
        bool isActive = false,
        DateTime? createdAt = null) => new()
    {
        Id        = Guid.NewGuid(),
        Name      = name,
        IsActive  = isActive,
        CreatedAt = createdAt ?? DateTime.UtcNow,
    };

    public static MealPlanRecipe MealPlanRecipe(
        Guid mealPlanId,
        Guid recipeId,
        DateOnly? scheduledDate = null,
        string portionSize = PortionSize.Regular,
        int displayOrder = 1) => new()
    {
        Id            = Guid.NewGuid(),
        MealPlanId    = mealPlanId,
        RecipeId      = recipeId,
        ScheduledDate = scheduledDate,
        PortionSize   = portionSize,
        DisplayOrder  = displayOrder,
    };

    public static CreateMealPlanRequest CreateMealPlanRequest(string name = "Test Plan") => new(name);

    public static AddMealPlanRecipeRequest AddMealPlanRecipeRequest(
        Guid recipeId,
        DateOnly? scheduledDate = null,
        string? portionSize = null) => new(recipeId, scheduledDate, portionSize);
}
