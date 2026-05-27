namespace RecipeApp.API.Models;

/// <summary>
/// A stored recipe with its ingredient list and step-by-step instructions.
/// Base quantities are always stored at the regular (1×) portion size.
/// Portion scaling (0.5×, 1×, 2×) is applied at query/display time only.
/// </summary>
public class Recipe
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Path/URL to the recipe image (local upload or external URL).</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Original web URL if the recipe was scraped.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Base serving count that the ingredient amounts correspond to.</summary>
    public int Servings { get; set; } = 4;

    /// <summary>Updated whenever this recipe is marked as cooked via a meal plan.</summary>
    public DateTime? LastCookedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<RecipeIngredient> Ingredients { get; set; } = [];
    public ICollection<RecipeStep> Steps { get; set; } = [];
}
