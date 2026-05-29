namespace RecipeApp.API.DTOs.Recipes;

public record RecipeIngredientResponse(
    Guid Id,
    Guid IngredientId,
    string IngredientName,
    string IngredientDisplayName,
    string Category,
    decimal Amount,
    string Unit,
    string? Notes,
    int DisplayOrder
);
