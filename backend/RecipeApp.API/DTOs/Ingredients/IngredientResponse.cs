namespace RecipeApp.API.DTOs.Ingredients;

/// <param name="HasHandSetDensity">The density, null included, was set by hand and is never replaced by the curated seed.</param>
/// <param name="RemovedAt">When it was removed; null for an active Ingredient.</param>
public record IngredientResponse(
    Guid Id,
    string Name,
    string DisplayName,
    string Category,
    string[] Aliases,
    string? DefaultUnit,
    decimal? GramsPerMillilitre,
    bool HasHandSetDensity,
    bool IsStaple,
    DateTime? RemovedAt,
    DateTime CreatedAt
);
