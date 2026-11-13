namespace RecipeApp.API.Validators;

/// <summary>Bounds on the text a Scrape from pasted text accepts, mirrored by <c>frontend/src/constants/scrapeLimits.js</c> for the sheet's counter.</summary>
public static class ScrapeLimits
{
    /// <summary>Shorter than this, once trimmed, cannot hold a recipe's ingredients and steps.</summary>
    public const int TextMinLength = 20;

    /// <summary>Refuses a longer paste rather than cutting it short, so keep <c>RecipeScraping:MaxHtmlCharacters</c>, which truncates what the model reads, at least this large.</summary>
    public const int TextMaxLength = 30_000;
}
