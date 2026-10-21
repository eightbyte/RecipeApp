namespace RecipeApp.API.DTOs.Ingredients;

public record UpdateIngredientRequest(
    string DisplayName,
    string Category,
    string? DefaultUnit,
    decimal? GramsPerMillilitre = null
);
