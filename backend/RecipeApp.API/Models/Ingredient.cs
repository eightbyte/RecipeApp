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
    /// Alternate names that resolve to this catalogue entry — regional spellings ("capsicum" for
    /// "bell pepper"), plurals, and the corpus variants the Phase 9.3 build pass folded in.
    ///
    /// <para>Read by the exact-match lookup, so a known synonym never reaches the LLM matching
    /// pass. That is why this is a column rather than a detail of the seed artefact: an ordinary
    /// scrape saying "garbanzo beans" gets a dictionary hit instead of a model call.</para>
    ///
    /// <para>Every alias is stored normalised lowercase, and no alias may equal any entry's
    /// <see cref="Name"/> or appear on two entries — an ambiguous alias silently resolves to
    /// whichever row the dictionary happened to see first. The invariant is enforced in code
    /// (Phase 9.3 §4.7) because Postgres cannot express it at reasonable cost.</para>
    /// </summary>
    public string[] Aliases { get; set; } = [];

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
