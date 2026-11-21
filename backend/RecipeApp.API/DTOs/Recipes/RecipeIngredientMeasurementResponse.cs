namespace RecipeApp.API.DTOs.Recipes;

/// <summary>One row's US Measurement at the servings asked for, which the recipe view swaps in after scaling on the device.</summary>
public record RecipeIngredientMeasurementResponse(Guid RecipeIngredientId, decimal Amount, string Unit, bool FromSource);
