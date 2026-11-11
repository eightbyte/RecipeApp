namespace RecipeApp.API.DTOs.Scrape;

/// <param name="ImageUrl">The source page's photo, which the preview loads straight from the source site; null when the page offers none.</param>
public record ScrapePreviewResponse(
    string Name,
    string? Description,
    int Servings,
    string SourceUrl,
    List<ScrapePreviewIngredientResponse> Ingredients,
    List<ScrapePreviewStepResponse> Steps,
    string? ImageUrl = null
);

/// <param name="Amount">Null when the source line stated no quantity, so the preview shows an empty field rather than a fabricated number.</param>
/// <param name="Unit">Null exactly when <paramref name="Amount"/> is null.</param>
/// <param name="EntryUnit">The entry-only unit (such as <c>lb</c>) the Source Measurement states and converts to, which the preview opens the row in with <paramref name="SourceAmount"/>; null otherwise.</param>
/// <param name="AmountFromSteps">True when the list line stated no amount and the row was filled from its steps; shown in the preview only and never saved.</param>
/// <param name="StepAmounts">The amounts the steps state for a row whose list line stated none, which the preview lists so the user can check or enter a total; empty otherwise.</param>
public record ScrapePreviewIngredientResponse(
    Guid? IngredientId,
    string Name,
    string DisplayName,
    decimal? Amount,
    string? Unit,
    decimal? SourceAmount,
    string? SourceUnit,
    string? EntryUnit,
    string? Notes,
    bool IsNew,
    string SuggestedCategory,
    int DisplayOrder,
    bool AmountFromSteps,
    List<ScrapePreviewStepAmountResponse> StepAmounts
);

/// <summary>One amount a step states for an ingredient, as the step stated it.</summary>
public record ScrapePreviewStepAmountResponse(decimal? Amount, string? Unit);

public record ScrapePreviewStepResponse(
    int StepNumber,
    string Instruction,
    List<int> IngredientIndexes
);
