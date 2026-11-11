using System.Text.Json;
using System.Text.Json.Nodes;
using AngleSharp.Dom;

namespace RecipeApp.API.Services;

/// <summary>Reads the ingredient lines Mealime lists beside each step, which its JSON-LD leaves out, from the page's <c>__NEXT_DATA__</c>.</summary>
public class MealimeSiteReader : ISiteReader
{
    public IReadOnlyCollection<string> Hosts { get; } = ["mealime.com", "www.mealime.com"];

    public IReadOnlyDictionary<int, IReadOnlyList<string>>? ReadStepLines(IDocument page)
    {
        var script = page.QuerySelector("script#__NEXT_DATA__");
        if (script is null || string.IsNullOrWhiteSpace(script.TextContent)) return null;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(script.TextContent);
        }
        catch (JsonException)
        {
            return null;
        }

        if (root?["props"]?["pageProps"]?["publishedRecipe"]?["instructions"] is not JsonArray instructions)
            return null;

        var lines = new Dictionary<int, IReadOnlyList<string>>();
        for (var i = 0; i < instructions.Count; i++)
        {
            if (instructions[i] is not JsonObject step) return null;

            var message = step["secondary_message"];
            if (message is null) continue;
            if (message is not JsonValue value || !value.TryGetValue<string>(out var text)) return null;

            var stepLines = text
                .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (stepLines.Length > 0) lines[i + 1] = stepLines;
        }

        return lines;
    }
}
