namespace RecipeApp.API.DTOs.Ingredients;

/// <param name="DisplayName">The Ingredient's <c>Name</c> is this, normalised.</param>
public record CreateIngredientRequest(
    string DisplayName,
    string Category,
    string? DefaultUnit,
    decimal? GramsPerMillilitre = null,
    bool IsStaple = false,
    IReadOnlyList<string>? Aliases = null
);
