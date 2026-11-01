namespace RecipeApp.API.DTOs.MealPlans;

public record UpdatePlannedMealRequest(
    DateOnly? ScheduledDate,
    string BatchSize
);
