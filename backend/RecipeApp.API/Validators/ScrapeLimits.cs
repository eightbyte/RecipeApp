namespace RecipeApp.API.Validators;

/// <summary>Bounds on the text a Scrape from pasted text accepts, mirrored by <c>frontend/src/constants/scrapeLimits.js</c> for the sheet's counter.</summary>
public static class ScrapeLimits
{
    /// <summary>Shorter than this, once trimmed, cannot hold a recipe's ingredients and steps.</summary>
    public const int TextMinLength = 20;

    /// <summary>Matches the most page text a URL Scrape sends the model, so a paste is refused rather than cut short.</summary>
    public const int TextMaxLength = 30_000;
}
