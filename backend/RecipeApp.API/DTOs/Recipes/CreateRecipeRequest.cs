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

public record RecipeIngredientRequest(
    Guid IngredientId,
    decimal Amount,
    string Unit,
    string? Notes,
    int DisplayOrder
);
