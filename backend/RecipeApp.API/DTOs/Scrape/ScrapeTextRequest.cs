namespace RecipeApp.API.DTOs.Scrape;

/// <param name="Text">A recipe pasted by the user, read by the model as it stands.</param>
public record ScrapeTextRequest(string Text);
