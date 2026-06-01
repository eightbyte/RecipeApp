using RecipeApp.API.DTOs.Recipes;
using RecipeApp.API.DTOs.Scrape;

namespace RecipeApp.API.Services;

public interface IRecipeScrapeService
{
    Task<ScrapePreviewResponse> ScrapeAsync(string url, CancellationToken ct = default);
    Task<RecipeDetailResponse> ConfirmAsync(ScrapeConfirmRequest request, CancellationToken ct = default);
}
