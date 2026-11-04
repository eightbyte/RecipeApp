namespace RecipeApp.API.DTOs.Scrape;

public record ScrapePreviewResponse(
    string Name,
    string? Description,
    int Servings,
    string SourceUrl,
    List<ScrapePreviewIngredientResponse> Ingredients,
    List<ScrapePreviewStepResponse> Steps
);

/// <param name="Amount">Null when the source line stated no quantity, so the preview shows an empty field rather than a fabricated number.</param>
/// <param name="Unit">Null exactly when <paramref name="Amount"/> is null.</param>
public record ScrapePreviewIngredientResponse(
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

public record ScrapePreviewStepResponse(
    int StepNumber,
    string Instruction,
    List<int> IngredientIndexes
);
