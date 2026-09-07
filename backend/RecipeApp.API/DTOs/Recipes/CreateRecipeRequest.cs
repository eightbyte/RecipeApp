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
public record RecipeIngredientRequest(
    Guid IngredientId,
    decimal Amount,
    string Unit,
    string? Notes,
    int DisplayOrder,
    decimal? SourceAmount = null,
    string? SourceUnit = null
);
