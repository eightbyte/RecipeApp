namespace RecipeApp.API.Models;

/// <summary>
/// Links a specific ingredient (with amount and unit) to a recipe.
/// Amount is stored at base (1× regular) portion.
/// </summary>
public class RecipeIngredient
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public Guid IngredientId { get; set; }

    /// <summary>Amount at base (regular) portion size, in metric units.</summary>
    public decimal Amount { get; set; }

    /// <summary>Canonical storable unit — see <see cref="Enums.MeasurementUnit"/>.</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Amount exactly as stated by the source recipe, before conversion. Null for hand-entered rows.</summary>
    public decimal? SourceAmount { get; set; }

    /// <summary>Unit exactly as stated by the source recipe (e.g. "cups", "ounce"). Null for hand-entered rows.</summary>
    public string? SourceUnit { get; set; }

    /// <summary>Optional prep note shown with the ingredient (e.g. "finely chopped").</summary>
    public string? Notes { get; set; }

    /// <summary>Display order within the ingredient list.</summary>
    public int DisplayOrder { get; set; }

    // Navigation
    public Recipe Recipe { get; set; } = null!;
    public Ingredient Ingredient { get; set; } = null!;
    public ICollection<RecipeStepIngredient> StepIngredients { get; set; } = [];
}
