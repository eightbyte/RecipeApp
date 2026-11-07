namespace RecipeApp.API.Models;

public class PlannedMeal
{
    public Guid Id { get; set; }
    public Guid MealPlanId { get; set; }
    public Guid RecipeId { get; set; }

    /// <summary>Nullable calendar date, because a Planned Meal may be Unscheduled.</summary>
    public DateOnly? ScheduledDate { get; set; }

    /// <summary>How many servings to make, kept when the recipe's own Servings is edited later.</summary>
    public int Servings { get; set; }

    /// <summary>Derived from insertion order; final sort uses dated-first then this value.</summary>
    public int DisplayOrder { get; set; }

    // Navigation
    public MealPlan MealPlan { get; set; } = null!;
    public Recipe Recipe { get; set; } = null!;
}
