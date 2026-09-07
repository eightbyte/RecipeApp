namespace RecipeApp.API.Models;

/// <summary>
/// Global ingredient catalogue entry.
/// Name is stored normalised (lowercase) for deduplication.
/// DisplayName is the human-readable form shown in the UI.
/// </summary>
public class Ingredient
{
    public Guid Id { get; set; }

    /// <summary>Normalised lowercase name used for lookups (e.g. "onion").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable display name (e.g. "Onion").</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Shopping list group — see <see cref="Enums.IngredientCategory"/>.</summary>
    public string Category { get; set; } = "OTHER";

    /// <summary>Suggested default unit — see <see cref="Enums.MeasurementUnit"/>.</summary>
    public string? DefaultUnit { get; set; }

    /// <summary>
    /// Bulk density in grams per millilitre, used to convert volume measurements
    /// (notably cups) to mass. Null means no reliable density is known for this
    /// ingredient — volume measurements are then kept as stated. See Phase 8.5.1 §5.3.
    /// </summary>
    public decimal? GramsPerMillilitre { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<RecipeIngredient> RecipeIngredients { get; set; } = [];
}
