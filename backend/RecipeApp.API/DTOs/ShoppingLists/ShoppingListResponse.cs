namespace RecipeApp.API.DTOs.ShoppingLists;

public record ShoppingListResponse(
    Guid Id,
    Guid MealPlanId,
    string MealPlanName,
    DateTime GeneratedAt,
    bool IsStale,
    List<ShoppingListItemResponse> Items
);

public record ShoppingListItemResponse(
    Guid Id,
    Guid? IngredientId,
    string DisplayName,
    string Category,
    decimal? Amount,
    string? Unit,
    bool IsChecked,
    bool IsCustom,
    bool NeedsReview,
    int DisplayOrder
);

public record AddCustomItemRequest(
    string Name,
    decimal? Amount,
    string? Unit,
    string? Category
);

public record UpdateItemRequest(
    bool IsChecked,
    decimal? Amount,
    string? Unit
);
