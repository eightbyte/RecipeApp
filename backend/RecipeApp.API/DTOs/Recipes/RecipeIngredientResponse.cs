namespace RecipeApp.API.DTOs.Recipes;

/// <param name="Amount">Null when the ingredient has no stated quantity (Phase 9.1 §3.1).</param>
/// <param name="Unit">Null exactly when <paramref name="Amount"/> is null.</param>
public record RecipeIngredientResponse(
    Guid Id,
    Guid IngredientId,
    string IngredientName,
    string IngredientDisplayName,
    string Category,
    decimal? Amount,
    string? Unit,
    decimal? SourceAmount,
    string? SourceUnit,
    string? Notes,
    int DisplayOrder
);
