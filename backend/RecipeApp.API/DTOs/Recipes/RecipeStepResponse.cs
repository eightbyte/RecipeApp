namespace RecipeApp.API.DTOs.Recipes;

public record RecipeStepResponse(
    Guid Id,
    int StepNumber,
    string Instruction,
    /// <summary>IDs of the RecipeIngredients (not Ingredient catalogue IDs) used in this step.</summary>
    List<Guid> RecipeIngredientIds
);
