namespace RecipeApp.API.DTOs.Recipes;

public record RecipeDetailResponse(
    Guid Id,
    string Name,
    string? Description,
    string? ImageUrl,
    string? SourceUrl,
    int Servings,
    DateTime? LastCookedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<RecipeIngredientResponse> Ingredients,
    List<RecipeStepResponse> Steps
);
