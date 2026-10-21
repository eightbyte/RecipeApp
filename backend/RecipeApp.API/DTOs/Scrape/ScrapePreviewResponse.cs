namespace RecipeApp.API.DTOs.Scrape;

public record ScrapePreviewResponse(
    string Name,
    string? Description,
    int Servings,
    string SourceUrl,
    List<ScrapePreviewIngredient> Ingredients,
    List<ScrapePreviewStep> Steps
);

/// <param name="Amount">
/// Null when the source line stated no quantity — the preview shows an empty amount field rather
/// than a fabricated number (Phase 9.1 §3.4).
/// </param>
/// <param name="Unit">Null exactly when <paramref name="Amount"/> is null.</param>
public record ScrapePreviewIngredient(
    Guid? IngredientId,
    string Name,
    string DisplayName,
    decimal? Amount,
    string? Unit,
    decimal? SourceAmount,
    string? SourceUnit,
    string? Notes,
    bool IsNew,
    string SuggestedCategory,
    int DisplayOrder
);

public record ScrapePreviewStep(
    int StepNumber,
    string Instruction,
    List<int> IngredientIndexes
);
