using RecipeApp.API.DTOs.Recipes;

namespace RecipeApp.API.DTOs.Scrape;

/// <param name="PhotoSkipped">True when the request named a photo that could not be downloaded or stored, so the recipe saved without one.</param>
public record ScrapeConfirmResponse(RecipeDetailResponse Recipe, bool PhotoSkipped);
