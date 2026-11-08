namespace RecipeApp.API.DTOs.Recipes;

public record CreateRecipeRequest(
    string Name,
    string? Description,
    string? SourceUrl,
    int Servings,
    List<RecipeIngredientRequest> Ingredients,
    List<RecipeStepRequest> Steps
);

/// <param name="IngredientIndexes">0-based indexes into the <see cref="CreateRecipeRequest.Ingredients"/> list, resolved to RecipeIngredient IDs after creation.</param>
public record RecipeStepRequest(
    int StepNumber,
    string Instruction,
    List<int> IngredientIndexes
);

/// <param name="SourceAmount">Amount as originally stated by an imported source, null for hand-entered rows and round-tripped by the recipe form to keep provenance.</param>
/// <param name="Amount">Null when the ingredient has no stated quantity, and null exactly when <paramref name="Unit"/> is.</param>
/// <param name="Unit">Null when <paramref name="Amount"/> is null; otherwise a canonical storable unit, or an entry-only unit converted on save with this measurement kept as the source.</param>
public record RecipeIngredientRequest(
    Guid IngredientId,
    decimal? Amount,
    string? Unit,
    string? Notes,
    int DisplayOrder,
    decimal? SourceAmount = null,
    string? SourceUnit = null
);
