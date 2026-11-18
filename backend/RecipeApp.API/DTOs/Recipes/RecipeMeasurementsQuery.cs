using Microsoft.AspNetCore.Mvc;

namespace RecipeApp.API.DTOs.Recipes;

/// <summary>The query string of <c>GET /recipes/{id}/measurements</c>, bound with <c>[AsParameters]</c>.</summary>
/// <param name="Servings">The servings the Measurements are scaled to, possibly fractional.</param>
/// <param name="System">Must be <c>UnitSystem.Us</c>, the only Unit System whose Measurements are computed rather than stored.</param>
public record RecipeMeasurementsQuery(
    [FromQuery(Name = "servings")] decimal? Servings,
    [FromQuery(Name = "system")]   string? System
);
