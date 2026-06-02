namespace RecipeApp.API.DTOs.MealPlans;

public record UpdateMealPlanRecipeRequest(
    DateOnly? ScheduledDate,
    string PortionSize
);
