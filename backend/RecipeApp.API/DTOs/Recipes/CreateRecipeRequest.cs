namespace RecipeApp.API.DTOs.Recipes;

public record CreateRecipeRequest(
    string Name,
    string? Description,
    string? SourceUrl,
    int Servings,
    List<RecipeIngredientRequest> Ingredients,
    List<RecipeStepRequest> Steps
);

/// <param name="IngredientIndexes">
/// 0-based indexes into the <see cref="CreateRecipeRequest.Ingredients"/> list.
/// The service resolves these to actual RecipeIngredient IDs after creation.
/// </param>
public record RecipeStepRequest(
    int StepNumber,
    string Instruction,
    List<int> IngredientIndexes
);

/// <param name="SourceAmount">
/// Amount as originally stated by an imported source. Null for hand-entered rows.
/// Round-tripped by the recipe form so editing an imported recipe keeps its provenance.
/// </param>
/// <param name="SourceUnit">Unit as originally stated by an imported source. Null for hand-entered rows.</param>
/// <param name="Amount">
/// Null when the ingredient has no stated quantity (Phase 9.1 §3.1). Must be null exactly when
/// <paramref name="Unit"/> is null — the validator rejects a half-set pair.
/// </param>
/// <param name="Unit">Null when <paramref name="Amount"/> is null; otherwise a canonical storable unit.</param>
public record RecipeIngredientRequest(
    Guid IngredientId,
    decimal? Amount,
    string? Unit,
    string? Notes,
    int DisplayOrder,
    decimal? SourceAmount = null,
    string? SourceUnit = null
);
