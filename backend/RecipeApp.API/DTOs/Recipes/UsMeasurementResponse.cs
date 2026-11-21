namespace RecipeApp.API.DTOs.Recipes;

/// <summary>A Recipe Ingredient's Measurement in US units at the servings asked for, unrounded so the client formats it.</summary>
/// <param name="FromSource">True when read from the row's US Source Measurement, so the view hides that source rather than repeat it.</param>
public record UsMeasurementResponse(decimal Amount, string Unit, bool FromSource);
