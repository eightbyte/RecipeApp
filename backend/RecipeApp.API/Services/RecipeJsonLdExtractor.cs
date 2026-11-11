using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AngleSharp.Dom;

namespace RecipeApp.API.Services;

/// <summary>Looks for a schema.org "Recipe" object in a page's JSON-LD blocks and renders it as clean plain text for the LLM prompt, which is checked before visible-text stripping.</summary>
public static class RecipeJsonLdExtractor
{
    /// <summary>The heading over a Site Reader's lines beneath a step, which the extraction prompt names.</summary>
    public const string StepLinesHeading = "Amounts for this step:";

    /// <param name="stepLines">A Site Reader's extra lines keyed by 1-based step number, counting steps but not section headings.</param>
    public static string? TryBuildRecipeText(
        IDocument document, IReadOnlyDictionary<int, IReadOnlyList<string>>? stepLines = null)
    {
        foreach (var recipe in EnumerateRecipeNodes(document))
        {
            var text = BuildText(recipe, stepLines);
            if (text is not null) return text;
        }

        return null;
    }

    /// <summary>The first schema.org Recipe object in the page's JSON-LD, or null if there is none, exposed for callers that need a specific field off the node.</summary>
    public static JsonNode? TryFindRecipeNode(IDocument document) =>
        EnumerateRecipeNodes(document).FirstOrDefault();

    /// <summary>Every non-blank URL in the <c>image</c> field of the page's first Recipe object, in order, whether the field is a string, an ImageObject or a list of either.</summary>
    public static IEnumerable<string> ImageUrlsOf(IDocument document)
    {
        JsonNode?[] images = TryFindRecipeNode(document)?["image"] switch
        {
            null => [],
            JsonArray list => list.ToArray(),
            var single => [single],
        };

        foreach (var image in images)
        {
            var url = image switch
            {
                JsonValue value when value.TryGetValue<string>(out var s) => s,
                JsonObject obj when obj["url"] is JsonValue value && value.TryGetValue<string>(out var s) => s,
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(url)) yield return url.Trim();
        }
    }

    /// <summary>Every Recipe node across the page's JSON-LD blocks in document order, lazily so callers that reject the first candidate only pay to parse the next.</summary>
    private static IEnumerable<JsonNode> EnumerateRecipeNodes(IDocument document)
    {
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            var content = script.TextContent;
            if (string.IsNullOrWhiteSpace(content)) continue;

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(content);
            }
            catch (JsonException)
            {
                // Real-world JSON-LD often has raw line breaks inside string values, which strict JSON forbids, so collapsing control characters to spaces recovers the payload.
                try
                {
                    root = JsonNode.Parse(Regex.Replace(content, "[\r\n\t]+", " "));
                }
                catch (JsonException)
                {
                    continue;
                }
            }

            if (root is not null && FindRecipeNode(root) is { } recipe)
                yield return recipe;
        }
    }

    // ── Locating the Recipe node ────────────────────────────────────────────────

    private static JsonNode? FindRecipeNode(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (IsRecipeType(obj["@type"])) return obj;
                if (obj["@graph"] is JsonArray graph)
                    foreach (var item in graph)
                        if (item is not null && FindRecipeNode(item) is { } fromGraph)
                            return fromGraph;
                return null;

            case JsonArray arr:
                foreach (var item in arr)
                    if (item is not null && FindRecipeNode(item) is { } fromArray)
                        return fromArray;
                return null;

            default:
                return null;
        }
    }

    private static bool IsRecipeType(JsonNode? typeNode) => typeNode switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => string.Equals(s, "Recipe", StringComparison.OrdinalIgnoreCase),
        JsonArray arr => arr.Any(t => t is JsonValue tv && tv.TryGetValue<string>(out var s)
                                       && string.Equals(s, "Recipe", StringComparison.OrdinalIgnoreCase)),
        _ => false,
    };

    // ── Rendering the Recipe node as plain text ─────────────────────────────────

    private static string? BuildText(JsonNode recipe, IReadOnlyDictionary<int, IReadOnlyList<string>>? stepLines)
    {
        var ingredients   = ExtractIngredients(recipe["recipeIngredient"]);
        var instructions  = ExtractInstructions(recipe["recipeInstructions"]);

        // Without both, this isn't usable — let the caller fall back to visible-text stripping.
        if (ingredients.Count == 0 || instructions.Count == 0) return null;

        var sb = new StringBuilder();

        var name = AsCleanString(recipe["name"]);
        if (!string.IsNullOrWhiteSpace(name)) sb.AppendLine($"Recipe Name: {name}");

        var description = AsCleanString(recipe["description"]);
        if (!string.IsNullOrWhiteSpace(description)) sb.AppendLine($"Description: {description}");

        var yield = AsCleanString(recipe["recipeYield"]);
        if (!string.IsNullOrWhiteSpace(yield)) sb.AppendLine($"Servings: {yield}");

        sb.AppendLine();
        sb.AppendLine("Ingredients:");
        foreach (var ingredient in ingredients)
            sb.AppendLine($"- {ingredient}");

        sb.AppendLine();
        sb.AppendLine("Instructions:");
        var stepNumber = 0;
        for (int i = 0; i < instructions.Count; i++)
        {
            var (text, isSection) = instructions[i];
            sb.AppendLine($"{i + 1}. {text}");
            if (isSection) continue;

            stepNumber++;
            if (stepLines is not null && stepLines.TryGetValue(stepNumber, out var lines))
                AppendStepLines(sb, StepLinesHeading, lines, indent: "   ");
        }

        return sb.ToString();
    }

    /// <summary>A Site Reader's lines for a page with no JSON-LD to place them in, as a section to follow the page text.</summary>
    public static string BuildStepLinesSection(IReadOnlyDictionary<int, IReadOnlyList<string>>? stepLines)
    {
        if (stepLines is null || stepLines.Count == 0) return string.Empty;

        var sb = new StringBuilder("\n\n");
        foreach (var (stepNumber, lines) in stepLines.OrderBy(s => s.Key))
            AppendStepLines(sb, $"Step {stepNumber}: {StepLinesHeading}", lines, indent: "");
        return sb.ToString();
    }

    private static void AppendStepLines(StringBuilder sb, string heading, IReadOnlyList<string> lines, string indent)
    {
        sb.AppendLine($"{indent}{heading}");
        foreach (var line in lines)
            sb.AppendLine($"{indent}- {line}");
    }

    private static List<string> ExtractIngredients(JsonNode? node)
    {
        var result = new List<string>();
        if (node is not JsonArray arr) return result;

        foreach (var item in arr)
        {
            var text = AsCleanString(item);
            if (!string.IsNullOrWhiteSpace(text)) result.Add(text);
        }
        return result;
    }

    /// <summary>Every instruction entry in order, marking the section headings that are not steps themselves.</summary>
    private static List<(string Text, bool IsSection)> ExtractInstructions(JsonNode? node)
    {
        var result = new List<(string Text, bool IsSection)>();
        switch (node)
        {
            case JsonArray arr:
                foreach (var item in arr)
                    CollectInstruction(item, result);
                break;

            case JsonValue:
                var text = AsCleanString(node);
                if (!string.IsNullOrWhiteSpace(text)) result.Add((text, false));
                break;
        }
        return result;
    }

    private static void CollectInstruction(JsonNode? item, List<(string Text, bool IsSection)> result)
    {
        switch (item)
        {
            case JsonValue:
                var text = AsCleanString(item);
                if (!string.IsNullOrWhiteSpace(text)) result.Add((text, false));
                break;

            case JsonObject obj:
                var type = AsCleanString(obj["@type"]);
                if (string.Equals(type, "HowToSection", StringComparison.OrdinalIgnoreCase)
                    && obj["itemListElement"] is JsonArray sectionItems)
                {
                    var sectionName = AsCleanString(obj["name"]);
                    if (!string.IsNullOrWhiteSpace(sectionName)) result.Add(($"[{sectionName}]", true));
                    foreach (var sub in sectionItems)
                        CollectInstruction(sub, result);
                }
                else
                {
                    var stepText = AsCleanString(obj["text"]) ?? AsCleanString(obj["name"]);
                    if (!string.IsNullOrWhiteSpace(stepText)) result.Add((stepText, false));
                }
                break;
        }
    }

    // ── Text cleanup ─────────────────────────────────────────────────────────────

    /// <summary>Reads a JSON-LD value as text, HTML-decoded, tag-stripped and whitespace-collapsed, taking the first entry of an array value.</summary>
    private static string? AsCleanString(JsonNode? node)
    {
        string? raw = node switch
        {
            null => null,
            JsonArray arr => arr.Count > 0 ? AsCleanString(arr[0]) : null,
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            JsonValue => node.ToString(),
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(raw)) return null;

        var decoded = WebUtility.HtmlDecode(raw);
        var withoutTags = Regex.Replace(decoded, "<[^>]+>", " ");
        var collapsed = Regex.Replace(withoutTags, @"[\r\n\t]+|\s{2,}", " ").Trim();
        return collapsed.Length == 0 ? null : collapsed;
    }
}
