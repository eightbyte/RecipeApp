namespace RecipeApp.API.Models;

/// <summary>Links a specific ingredient, with its amount and unit stored at the Regular Batch Size, to a recipe.</summary>
public class RecipeIngredient
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public Guid IngredientId { get; set; }

    /// <summary>Amount at the Regular Batch Size in metric units, where null means the source states no quantity; never zero, which would sum into shopping lists.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Canonical storable unit (see <see cref="Enums.MeasurementUnit"/>), null exactly when <see cref="Amount"/> is null, and <c>pcs</c> for an amount stated with no unit.</summary>
    public string? Unit { get; set; }

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

    /// <summary>Pairs an amount with its unit for storage, dropping the unit whenever the amount is null and storing an amount with no unit as a count of <c>pcs</c>.</summary>
    public static (decimal? Amount, string? Unit) ToStoredMeasurement(decimal? amount, string? unit) =>
        amount.HasValue
            ? (amount, string.IsNullOrWhiteSpace(unit) ? Enums.MeasurementUnit.Piece : unit.Trim())
            : (null, null);
}
