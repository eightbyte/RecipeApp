namespace RecipeApp.API.Models;

/// <summary>A global ingredient catalogue entry, stored normalised (lowercase) in <c>Name</c> with the human-readable <c>DisplayName</c> shown in the UI.</summary>
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

    /// <summary>Alternate names that resolve to this entry, stored normalised, never equal to any entry's <see cref="Name"/> and never shared between entries, an invariant enforced in code.</summary>
    public string[] Aliases { get; set; } = [];

    /// <summary>Bulk density in grams per millilitre used to convert volume to mass, where null means no reliable density is known and volume measurements are kept as stated.</summary>
    public decimal? GramsPerMillilitre { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<RecipeIngredient> RecipeIngredients { get; set; } = [];
}
