namespace RecipeApp.API.DTOs.Scrape;

public record ScrapeConfirmRequest(
    string Name,
    string? Description,
    string SourceUrl,
    int Servings,
    List<ScrapeConfirmIngredientRequest> Ingredients,
    List<ScrapeConfirmStepRequest> Steps
);

/// <param name="Amount">Null when the source line stated no quantity.</param>
/// <param name="Unit">Null exactly when <paramref name="Amount"/> is null.</param>
public record ScrapeConfirmIngredientRequest(
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

public record ScrapeConfirmStepRequest(
    int StepNumber,
    string Instruction,
    List<int> IngredientIndexes
);
