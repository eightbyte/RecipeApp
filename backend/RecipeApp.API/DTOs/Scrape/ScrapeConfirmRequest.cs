namespace RecipeApp.API.DTOs.Scrape;

/// <param name="ImageUrl">The preview's remote photo, downloaded on confirm; null when the page had none or the user removed it.</param>
public record ScrapeConfirmRequest(
    string Name,
    string? Description,
    string SourceUrl,
    int Servings,
    List<ScrapeConfirmIngredientRequest> Ingredients,
    List<ScrapeConfirmStepRequest> Steps,
    string? ImageUrl = null
);

/// <param name="Amount">Null when the source line stated no quantity.</param>
/// <param name="Unit">Null exactly when <paramref name="Amount"/> is null; an entry-only unit is converted on save with this measurement kept as the source.</param>
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
