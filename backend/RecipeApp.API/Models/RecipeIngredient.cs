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

    /// <summary>
    /// Amount at base (regular) portion size, in metric units. <b>Null means the source states no
    /// quantity</b> — <c>salt</c>, <c>nonstick cooking spray</c>, <c>raisins</c> — which is a real
    /// property of home cooking rather than a parse defect (Phase 9.1 §3.1). Null is never a
    /// placeholder for an unknown amount: it is the assertion that no amount was stated. Zero is
    /// not used for this, because zero sums into a shopping list and renders as <c>0 g Salt</c>.
    /// <para><see cref="Unit"/> is null exactly when this is null — a unit with no amount would
    /// be as fabricated as a gram figure derived from an unknown density.</para>
    /// </summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Canonical storable unit — see <see cref="Enums.MeasurementUnit"/>.
    /// Null exactly when <see cref="Amount"/> is null (Phase 9.1 §3.1).
    /// </summary>
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

    /// <summary>
    /// Pairs an amount with its unit for storage, holding the invariant that <see cref="Unit"/> is
    /// null whenever <see cref="Amount"/> is. A unit measures nothing on its own, so it is dropped
    /// alongside a missing amount.
    ///
    /// <para>The converse is deliberately <i>not</i> coerced: an amount with no unit is real data
    /// the caller stated, and both validators reject it loudly rather than having it quietly
    /// discarded here.</para>
    ///
    /// <para>Every persistence path runs through this, because <c>ConfirmAsync</c> performs no
    /// validation of its own — validation lives in the endpoint filter, which a service-level
    /// caller such as the Phase 9 seeder bypasses entirely (Phase 9.1 §1.2).</para>
    /// </summary>
    public static (decimal? Amount, string? Unit) ToStoredMeasurement(decimal? amount, string? unit) =>
        amount.HasValue ? (amount, unit?.Trim()) : (null, null);
}
