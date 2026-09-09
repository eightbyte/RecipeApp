namespace RecipeApp.API.DTOs.Scrape;

public record ScrapeConfirmRequest(
    string Name,
    string? Description,
    string SourceUrl,
    int Servings,
    List<ScrapeConfirmIngredient> Ingredients,
    List<ScrapeConfirmStep> Steps
);

/// <param name="Amount">
/// Null when the source line stated no quantity (Phase 9.1 §3.1). Must be null exactly when
/// <paramref name="Unit"/> is null.
/// </param>
/// <param name="Unit">Null exactly when <paramref name="Amount"/> is null.</param>
public record ScrapeConfirmIngredient(
    Guid? IngredientId,
    string? NewIngredientName,
    string? NewIngredientDisplayName,
    string? Category,
    decimal? Amount,
    string? Unit,
    string? Notes,
    int DisplayOrder,
    decimal? SourceAmount = null,
    string? SourceUnit = null
);

public record ScrapeConfirmStep(
    int StepNumber,
    string Instruction,
    List<int> IngredientIndexes
);
