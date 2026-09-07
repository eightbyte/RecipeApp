using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AngleSharp.Dom;

namespace RecipeApp.API.Services;

/// <summary>
/// Looks for a schema.org "Recipe" object inside a page's JSON-LD (&lt;script type="application/ld+json"&gt;)
/// blocks and, if found, renders it as a clean plain-text block for the LLM extraction prompt.
/// Most recipe sites embed this markup for Google's "rich results" even when the visible page is
/// otherwise a heavily scripted SPA shell, so this is checked before falling back to visible-text stripping.
/// </summary>
public static class RecipeJsonLdExtractor
{
    public static string? TryBuildRecipeText(IDocument document)
    {
        foreach (var recipe in EnumerateRecipeNodes(document))
        {
            var text = BuildText(recipe);
            if (text is not null) return text;
        }

        return null;
    }

    /// <summary>
    /// The first schema.org Recipe object in the page's JSON-LD, or null if there is none.
    /// Exposed for callers that need a specific field off the node (the seed harvester reads
    /// <c>image.url</c>) rather than the rendered prompt text.
    /// </summary>
    public static JsonNode? TryFindRecipeNode(IDocument document) =>
        EnumerateRecipeNodes(document).FirstOrDefault();

    /// <summary>
    /// Every Recipe node across the page's JSON-LD blocks, in document order. Lazy, so callers
    /// that reject the first candidate only pay to parse the next one.
    /// </summary>
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
                // Real-world JSON-LD is frequently malformed — the most common case seen in the
                // wild is raw, unescaped line breaks inside string values (e.g. a recipe step's
                // "text" wrapped across multiple source lines). Strict JSON forbids literal control
                // characters inside strings, but collapsing them to spaces is harmless everywhere
                // else in the document (JSON is whitespace-insensitive outside of strings) and
                // recovers the otherwise-valid payload.
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

    private static string? BuildText(JsonNode recipe)
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
        for (int i = 0; i < instructions.Count; i++)
            sb.AppendLine($"{i + 1}. {instructions[i]}");

        return sb.ToString();
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

    private static List<string> ExtractInstructions(JsonNode? node)
    {
        var result = new List<string>();
        switch (node)
        {
            case JsonArray arr:
                foreach (var item in arr)
                    CollectInstruction(item, result);
                break;

            case JsonValue:
                var text = AsCleanString(node);
                if (!string.IsNullOrWhiteSpace(text)) result.Add(text);
                break;
        }
        return result;
    }

    private static void CollectInstruction(JsonNode? item, List<string> result)
    {
        switch (item)
        {
            case JsonValue:
                var text = AsCleanString(item);
                if (!string.IsNullOrWhiteSpace(text)) result.Add(text);
                break;

            case JsonObject obj:
                var type = AsCleanString(obj["@type"]);
                if (string.Equals(type, "HowToSection", StringComparison.OrdinalIgnoreCase)
                    && obj["itemListElement"] is JsonArray sectionItems)
                {
                    var sectionName = AsCleanString(obj["name"]);
                    if (!string.IsNullOrWhiteSpace(sectionName)) result.Add($"[{sectionName}]");
                    foreach (var sub in sectionItems)
                        CollectInstruction(sub, result);
                }
                else
                {
                    var stepText = AsCleanString(obj["text"]) ?? AsCleanString(obj["name"]);
                    if (!string.IsNullOrWhiteSpace(stepText)) result.Add(stepText);
                }
                break;
        }
    }

    // ── Text cleanup ─────────────────────────────────────────────────────────────

    /// <summary>Reads a JSON-LD value as text, HTML-decodes it, strips any embedded tags, and collapses whitespace.
    /// Handles values that are themselves arrays (e.g. recipeYield: ["8", "8 servings"]) by taking the first entry.</summary>
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
