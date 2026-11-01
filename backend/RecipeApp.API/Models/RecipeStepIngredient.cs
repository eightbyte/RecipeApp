namespace RecipeApp.API.Models;

/// <summary>Junction table mapping which RecipeIngredients a RecipeStep uses, which drives the per-step ingredients display in cooking mode.</summary>
public class RecipeStepIngredient
{
    public Guid StepId { get; set; }
    public Guid RecipeIngredientId { get; set; }

    // Navigation
    public RecipeStep Step { get; set; } = null!;
    public RecipeIngredient RecipeIngredient { get; set; } = null!;
}
