namespace RecipeApp.API.Models;

public class MealPlan
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Only one row may be true at a time (enforced via partial unique index).</summary>
    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Null while active; set when the plan is deactivated.</summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary>Bumped when recipes are added, updated, or removed; used for stale-list detection.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<MealPlanRecipe> Recipes { get; set; } = [];
}
