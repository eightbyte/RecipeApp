namespace RecipeApp.API.Models;

public class PlannedMeal
{
    public Guid Id { get; set; }
    public Guid MealPlanId { get; set; }
    public Guid RecipeId { get; set; }

    /// <summary>Nullable calendar date, because a Planned Meal may be Unscheduled.</summary>
    public DateOnly? ScheduledDate { get; set; }

    public string BatchSize { get; set; } = Enums.BatchSize.Regular;

    /// <summary>Derived from insertion order; final sort uses dated-first then this value.</summary>
    public int DisplayOrder { get; set; }

    // Navigation
    public MealPlan MealPlan { get; set; } = null!;
    public Recipe Recipe { get; set; } = null!;
}
