namespace RecipeApp.API.DTOs.Ingredients;

public record IngredientResponse(
    Guid Id,
    string Name,
    string DisplayName,
    string Category,
    string[] Aliases,
    string? DefaultUnit,
    decimal? GramsPerMillilitre,
    bool IsStaple,
    DateTime CreatedAt
);
