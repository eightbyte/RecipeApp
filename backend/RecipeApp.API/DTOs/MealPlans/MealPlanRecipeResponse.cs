namespace RecipeApp.API.DTOs.MealPlans;

public record MealPlanRecipeResponse(
    Guid Id,
    Guid RecipeId,
    string RecipeName,
    string? RecipeImageUrl,
    DateTime? RecipeLastCookedAt,
    DateOnly? ScheduledDate,
    string PortionSize,
    int DisplayOrder
);
