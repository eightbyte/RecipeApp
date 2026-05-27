namespace RecipeApp.API.Models;

/// <summary>
/// A single step within a recipe's instructions.
/// Steps are ordered by <see cref="StepNumber"/> (1-based).
/// </summary>
public class RecipeStep
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }

    /// <summary>1-based step index.</summary>
    public int StepNumber { get; set; }

    /// <summary>Full instruction text for this step.</summary>
    public string Instruction { get; set; } = string.Empty;

    // Navigation
    public Recipe Recipe { get; set; } = null!;
    public ICollection<RecipeStepIngredient> StepIngredients { get; set; } = [];
}
