namespace RecipeApp.API.DTOs.MealPlans;

public record AddMealPlanRecipeRequest(
    Guid RecipeId,
    DateOnly? ScheduledDate,
    string? PortionSize
);
