namespace RecipeApp.API.DTOs.Ingredients;

public record CreateIngredientRequest(
    string Name,
    string DisplayName,
    string Category,
    string? DefaultUnit
);
