namespace RecipeApp.API.DTOs.Ingredients;

public record IngredientResponse(
    Guid Id,
    string Name,
    string DisplayName,
    string Category,
    string? DefaultUnit,
    DateTime CreatedAt
);
