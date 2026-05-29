namespace RecipeApp.API.DTOs.Recipes;

public record RecipeListItemResponse(
    Guid Id,
    string Name,
    string? Description,
    string? ImageUrl,
    int Servings,
    int IngredientCount,
    DateTime? LastCookedAt,
    DateTime CreatedAt
);
