namespace RecipeApp.API.Models;

/// <summary>
/// Junction table: maps which RecipeIngredients are used in a specific RecipeStep.
/// This drives the "ingredients used in this step" display during cooking mode.
/// </summary>
public class RecipeStepIngredient
{
    public Guid StepId { get; set; }
    public Guid RecipeIngredientId { get; set; }

    // Navigation
    public RecipeStep Step { get; set; } = null!;
    public RecipeIngredient RecipeIngredient { get; set; } = null!;
}
