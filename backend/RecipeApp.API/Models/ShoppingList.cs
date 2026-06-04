using RecipeApp.API.Enums;

namespace RecipeApp.API.Models;

public class ShoppingList
{
    public Guid Id { get; set; }
    public Guid MealPlanId { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public MealPlan MealPlan { get; set; } = null!;
    public ICollection<ShoppingListItem> Items { get; set; } = [];
}

public class ShoppingListItem
{
    public Guid Id { get; set; }
    public Guid ShoppingListId { get; set; }
    public Guid? IngredientId { get; set; }
    public string? CustomName { get; set; }
    public string Category { get; set; } = IngredientCategory.Other;
    public decimal? Amount { get; set; }
    public string? Unit { get; set; }
    public bool IsChecked { get; set; }
    public bool IsCustom { get; set; }
    public bool NeedsReview { get; set; }
    public int DisplayOrder { get; set; }

    public ShoppingList ShoppingList { get; set; } = null!;
    public Ingredient? Ingredient { get; set; }
}
