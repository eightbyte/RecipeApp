namespace RecipeApp.API.DTOs.Ingredients;

/// <param name="DisplayName">The Ingredient's <c>Name</c> is always this, normalised, so renaming changes what it matches.</param>
/// <param name="Aliases">Replaces every alias; null keeps the current ones.</param>
public record UpdateIngredientRequest(
    string DisplayName,
    string Category,
    string? DefaultUnit,
    decimal? GramsPerMillilitre = null,
    bool IsStaple = false,
    IReadOnlyList<string>? Aliases = null
);
