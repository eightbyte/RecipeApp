namespace RecipeApp.API.DTOs.Recipes;

/// <summary>One page of <c>GET /recipes</c>, kept non-generic because a generic type's name ends in <c>`1</c> and breaks the DTO suffix rule.</summary>
/// <param name="TotalCount">Every recipe that matches the filters, not just this page.</param>
public record RecipeListPageResponse(
    List<RecipeListItemResponse> Items,
    int TotalCount
);
