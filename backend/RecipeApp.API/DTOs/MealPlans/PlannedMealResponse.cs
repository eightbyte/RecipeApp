namespace RecipeApp.API.DTOs.MealPlans;

public record PlannedMealResponse(
    Guid Id,
    Guid RecipeId,
    string RecipeName,
    string? RecipeImageUrl,
    DateTime? RecipeLastCookedAt,
    DateOnly? ScheduledDate,
    string BatchSize,
    int DisplayOrder
);
