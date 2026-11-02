using Microsoft.AspNetCore.Mvc;

namespace RecipeApp.API.DTOs.Recipes;

/// <summary>The query string of <c>GET /recipes</c>, bound with <c>[AsParameters]</c>; the explicit names keep the documented parameters camelCase.</summary>
/// <param name="Sort">One of <c>RecipeSort.All</c>; null means <c>RecipeSort.Default</c>.</param>
/// <param name="Direction">One of <c>SortDirection.All</c>; null means <c>SortDirection.Default</c>.</param>
public record RecipeListQuery(
    [FromQuery(Name = "search")]            string? Search,
    [FromQuery(Name = "ingredient")]        string? Ingredient,
    [FromQuery(Name = "excludeRecentDays")] int? ExcludeRecentDays,
    [FromQuery(Name = "lastCookedBefore")]  DateTime? LastCookedBefore,
    [FromQuery(Name = "sort")]              string? Sort,
    [FromQuery(Name = "direction")]         string? Direction
);
