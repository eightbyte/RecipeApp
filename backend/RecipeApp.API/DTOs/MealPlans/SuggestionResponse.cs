namespace RecipeApp.API.DTOs.MealPlans;

public record SuggestionResponse(
    Guid RecipeId,
    string RecipeName,
    string? RecipeImageUrl,
    DateTime? RecipeLastCookedAt,
    int OverlapCount,
    List<string> OverlappingIngredients
);
