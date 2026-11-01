namespace RecipeApp.API.DTOs.MealPlans;

public record MealPlanListItemResponse(
    Guid Id,
    string Name,
    bool IsActive,
    int PlannedMealCount,
    DateOnly? FirstScheduledDate,
    DateOnly? LastScheduledDate,
    DateTime CreatedAt,
    DateTime? ClosedAt
);
