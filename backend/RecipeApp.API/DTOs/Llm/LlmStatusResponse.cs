using RecipeApp.API.Services.Llm;

namespace RecipeApp.API.DTOs.Llm;

/// <param name="Provider">The provider Scraping uses, so the frontend can say when a Scrape's content leaves the network.</param>
/// <param name="Model">The model's name when the provider is ready and reports one.</param>
public record LlmStatusResponse(LlmProvider Provider, LlmState State, string? Model);
