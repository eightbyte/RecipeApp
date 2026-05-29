namespace RecipeApp.API.DTOs.Recipes;

/// <summary>
/// Replaces all ingredients and steps on the recipe.
/// The IngredientIndexes in each step reference this request's Ingredients list (0-based).
/// </summary>
public record UpdateRecipeRequest(
    string Name,
    string? Description,
    string? SourceUrl,
    int Servings,
    List<RecipeIngredientRequest> Ingredients,
    List<RecipeStepRequest> Steps
);
