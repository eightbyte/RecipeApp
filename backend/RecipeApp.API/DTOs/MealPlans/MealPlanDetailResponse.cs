namespace RecipeApp.API.DTOs.MealPlans;

public record MealPlanDetailResponse(
    Guid Id,
    string Name,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? ClosedAt,
    List<MealPlanRecipeResponse> Recipes
);
