using Microsoft.AspNetCore.Mvc;

namespace RecipeApp.API.DTOs.Recipes;

/// <summary>The query string of <c>GET /recipes/{id}</c>, bound with <c>[AsParameters]</c>.</summary>
/// <param name="Servings">The servings the US Measurements are scaled to, possibly fractional; null means the recipe's Servings.</param>
/// <param name="System">One of <c>UnitSystem.All</c>; null means <c>UnitSystem.Default</c>. Only <c>us</c> adds US Measurements.</param>
public record RecipeDetailQuery(
    [FromQuery(Name = "servings")] decimal? Servings,
    [FromQuery(Name = "system")]   string? System
);
