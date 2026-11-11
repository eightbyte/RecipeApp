using AngleSharp.Dom;

namespace RecipeApp.API.Services;

/// <summary>A reader for one site's own page data, chosen by the URL's host, that adds detail the JSON-LD leaves out without replacing any of it.</summary>
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
