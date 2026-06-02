namespace RecipeApp.API.Models;

public class MealPlanRecipe
{
    public Guid Id { get; set; }
    public Guid MealPlanId { get; set; }
    public Guid RecipeId { get; set; }

    /// <summary>Nullable calendar date — undated meals are allowed.</summary>
    public DateOnly? ScheduledDate { get; set; }

    public string PortionSize { get; set; } = Enums.PortionSize.Regular;

    /// <summary>Derived from insertion order; final sort uses dated-first then this value.</summary>
    public int DisplayOrder { get; set; }

    // Navigation
    public MealPlan MealPlan { get; set; } = null!;
    public Recipe Recipe { get; set; } = null!;
}
