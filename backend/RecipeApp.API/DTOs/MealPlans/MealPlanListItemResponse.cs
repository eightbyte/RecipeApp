namespace RecipeApp.API.DTOs.MealPlans;

public record MealPlanListItemResponse(
    Guid Id,
    string Name,
    bool IsActive,
    int RecipeCount,
    DateOnly? FirstScheduledDate,
    DateOnly? LastScheduledDate,
    DateTime CreatedAt,
    DateTime? ClosedAt
);
