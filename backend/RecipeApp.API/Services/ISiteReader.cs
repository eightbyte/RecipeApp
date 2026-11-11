using AngleSharp.Dom;

namespace RecipeApp.API.Services;

/// <summary>
/// A Site Reader: a reader for one site's own page data, adding detail its JSON-LD leaves out. The scrape picks one by the
/// URL's host, and a reader never replaces what the JSON-LD gives or calls the model.
/// </summary>
public interface ISiteReader
{
    /// <summary>The hosts this reader handles, such as <c>www.mealime.com</c>, matched ignoring case.</summary>
    IReadOnlyCollection<string> Hosts { get; }

    /// <summary>
    /// Each step's extra lines keyed by its 1-based step number, leaving out steps that have none, or null when the page data
    /// is not in the shape the reader expects.
    /// </summary>
    IReadOnlyDictionary<int, IReadOnlyList<string>>? ReadStepLines(IDocument page);
}
