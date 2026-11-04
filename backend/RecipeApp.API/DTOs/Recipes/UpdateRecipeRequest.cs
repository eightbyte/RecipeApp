namespace RecipeApp.API.DTOs.Recipes;

/// <summary>Replaces all ingredients and steps on the recipe, with each step's IngredientIndexes referencing this request's 0-based Ingredients list.</summary>
public record UpdateRecipeRequest(
    string Name,
    string? Description,
    string? SourceUrl,
    int Servings,
    List<RecipeIngredientRequest> Ingredients,
    List<RecipeStepRequest> Steps
);
