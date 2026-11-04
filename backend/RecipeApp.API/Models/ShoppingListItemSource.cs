namespace RecipeApp.API.Models;

/// <summary>A Planned Meal that contributed to a Generated Item, snapshotted at generation so removing the Planned Meal leaves it intact.</summary>
public class ShoppingListItemSource
{
    public Guid Id { get; set; }
    public Guid ShoppingListItemId { get; set; }

    /// <summary>Null once the recipe is deleted, when <see cref="RecipeName"/> still names it.</summary>
    public Guid? RecipeId { get; set; }

    public string RecipeName { get; set; } = string.Empty;
    public string BatchSize { get; set; } = Enums.BatchSize.Regular;
    public DateOnly? ScheduledDate { get; set; }

    /// <summary>The contribution in the recipe's own unit, null for an Unquantified Ingredient.</summary>
    public decimal? Amount { get; set; }
    public string? Unit { get; set; }

    /// <summary>The contribution in the item's unit, null when that is already the recipe's own unit.</summary>
    public decimal? ShareAmount { get; set; }
    public string? ShareUnit { get; set; }

    public int DisplayOrder { get; set; }

    public ShoppingListItem ShoppingListItem { get; set; } = null!;
    public Recipe? Recipe { get; set; }
}
