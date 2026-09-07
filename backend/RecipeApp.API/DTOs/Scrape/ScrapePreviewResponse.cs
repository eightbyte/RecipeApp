namespace RecipeApp.API.DTOs.Scrape;

public record ScrapePreviewResponse(
    string Name,
    string? Description,
    int Servings,
    string SourceUrl,
    List<ScrapePreviewIngredient> Ingredients,
    List<ScrapePreviewStep> Steps
);

public record ScrapePreviewIngredient(
    Guid? IngredientId,
    string Name,
    string DisplayName,
    decimal Amount,
    string Unit,
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
