using AngleSharp.Dom;
using RecipeApp.API.Validators;

namespace RecipeApp.API.Services;

/// <summary>Finds the photo a scraped recipe page offers: the recipe's own JSON-LD image first, then the page's Open Graph image.</summary>
public static class RecipePhotoLocator
{
    /// <returns>The first usable photo as an absolute http(s) URL, or null when the page offers none.</returns>
    public static string? TryFind(IDocument document, Uri pageUrl)
    {
        var openGraph = document.QuerySelector("meta[property='og:image']")?.GetAttribute("content");
        var candidates = RecipeJsonLdExtractor.ImageUrlsOf(document).Append(openGraph);

        return candidates
            .Select(candidate => Resolve(candidate, pageUrl))
            .FirstOrDefault(url => url is not null);
    }

    /// <summary>A candidate as an absolute URL the confirm validator accepts, or null, which also drops a <c>data:</c> URL by its scheme.</summary>
    private static string? Resolve(string? candidate, Uri pageUrl)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;
        if (!Uri.TryCreate(pageUrl, candidate.Trim(), out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        var url = uri.AbsoluteUri;
        return url.Length <= RecipeLimits.ImageUrlMaxLength ? url : null;
    }
}
