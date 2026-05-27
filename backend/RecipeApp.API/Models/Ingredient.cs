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

    /// <summary>Suggested default unit (e.g. "g", "ml", "pcs").</summary>
    public string? DefaultUnit { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<RecipeIngredient> RecipeIngredients { get; set; } = [];
}
