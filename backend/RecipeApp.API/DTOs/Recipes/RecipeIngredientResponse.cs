namespace RecipeApp.API.DTOs.Recipes;

/// <param name="Amount">Null when the ingredient has no stated quantity.</param>
/// <param name="Unit">Null exactly when <paramref name="Amount"/> is null.</param>
/// <param name="UsMeasurement">Present only when the recipe is asked for in the US Unit System, and then null for a row that reads the same in every Unit System.</param>
/// <param name="EntryUnit">The entry-only unit (such as <c>oz</c>) the Source Measurement states and still converts to, which the recipe form reopens the row in with <paramref name="SourceAmount"/>; null otherwise.</param>
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
    string? EntryUnit,
    string? Notes,
    int DisplayOrder,
    UsMeasurementResponse? UsMeasurement = null
);
