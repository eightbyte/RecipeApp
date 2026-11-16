namespace RecipeApp.API.DTOs.Ingredients;

/// <param name="RecipeCount">Recipes with at least one line of this Ingredient.</param>
public record IngredientUsageResponse(int RecipeCount);
