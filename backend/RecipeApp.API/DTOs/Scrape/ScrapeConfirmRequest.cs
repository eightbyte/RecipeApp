namespace RecipeApp.API.DTOs.Scrape;

public record ScrapeConfirmRequest(
    string Name,
    string? Description,
    string SourceUrl,
    int Servings,
    List<ScrapeConfirmIngredient> Ingredients,
    List<ScrapeConfirmStep> Steps
);

public record ScrapeConfirmIngredient(
    Guid? IngredientId,
    string? NewIngredientName,
    string? NewIngredientDisplayName,
    string? Category,
    decimal Amount,
    string Unit,
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
