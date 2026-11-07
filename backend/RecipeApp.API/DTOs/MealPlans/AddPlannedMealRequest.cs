namespace RecipeApp.API.DTOs.MealPlans;

public record AddPlannedMealRequest(
    Guid RecipeId,
    DateOnly? ScheduledDate,
    int? Servings
);
